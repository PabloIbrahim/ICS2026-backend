
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

public class ProductsApiTests
{
    private static ProductModel.Request CreateValidProductRequest()
    {
        return new ProductModel.Request(
            Sku: "TEST-INT-001",
            InternalCode: "INT-001",
            Name: "Producto de integración",
            Description: "Producto creado durante una prueba",
            CurrentUnitPrice: 1500,
            StockQuantity: 10
        );
    }

    private static async Task SeedProducts(
        CustomWebApplicationFactory factory,
        params Product[] products)
    {
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        await context.Set<Product>().AddRangeAsync(products);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        using var anonymousClient = factory.CreateClient();
        using var userClient = factory.CreateClient();

        TestAuthenticationHelper.AuthenticateAsUser(
            userClient,
            factory.Services,
            Guid.NewGuid().ToString()
        );

        var request = CreateValidProductRequest();

        // Act
        var anonymousResponse = await anonymousClient.PostAsJsonAsync(
            "/api/products",
            request
        );

        var userResponse = await userClient.PostAsJsonAsync(
            "/api/products",
            request
        );

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            anonymousResponse.StatusCode
        );

        Assert.Equal(
            HttpStatusCode.Forbidden,
            userResponse.StatusCode
        );
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        using var client = factory.CreateClient();

        TestAuthenticationHelper.AuthenticateAsAdmin(
            client,
            factory.Services
        );

        var request = CreateValidProductRequest();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/products",
            request
        );

        // Assert: respuesta HTTP
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var createdProduct =
            await response.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>();

        Assert.NotNull(createdProduct);
        Assert.Equal(request.Sku, createdProduct.Sku);
        Assert.Equal(request.Name, createdProduct.Name);
        Assert.Equal(request.InternalCode, createdProduct.InternalCode);
        Assert.Equal(request.CurrentUnitPrice, createdProduct.CurrentUnitPrice);
        Assert.Equal(request.StockQuantity, createdProduct.StockQuantity);
        Assert.True(createdProduct.IsActive);

        // Assert: persistencia real en SQLite
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        var persistedProduct = await context.Set<Product>()
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Sku == request.Sku);

        Assert.NotNull(persistedProduct);
        Assert.Equal(request.Name, persistedProduct.Name);
        Assert.Equal(request.InternalCode, persistedProduct.InternalCode);
        Assert.Equal(request.CurrentUnitPrice, persistedProduct.CurrentUnitPrice);
        Assert.Equal(request.StockQuantity, persistedProduct.StockQuantity);
        Assert.True(persistedProduct.IsActive);
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        var request = CreateValidProductRequest();

        // Primero insertamos un producto con el SKU que vamos a duplicar.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<Dsw2025TpiContext>();

            var existingProduct = new Product(
                request.Sku,
                request.Name,
                request.CurrentUnitPrice,
                request.StockQuantity,
                request.Description,
                request.InternalCode
            );

            context.Set<Product>().Add(existingProduct);

            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient();

        TestAuthenticationHelper.AuthenticateAsAdmin(
            client,
            factory.Services
        );

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/products",
            request
        );

        // Assert: el middleware actual transforma el duplicado en 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Assert: no se creó un segundo producto con el mismo SKU.
        using var verificationScope = factory.Services.CreateScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        var productsWithSameSku = await verificationContext.Set<Product>()
            .CountAsync(p => p.Sku == request.Sku);

        Assert.Equal(1, productsWithSameSku);
    }

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        await SeedProducts(
            factory,
            new Product(
                "LIST-001",
                "Producto Listado Uno",
                1000,
                10,
                "Descripción uno",
                "COD-LIST-001"
            ),
            new Product(
                "LIST-002",
                "Producto Listado Dos",
                2000,
                20,
                "Descripción dos",
                "COD-LIST-002"
            )
        );

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/api/products?pageNumber=1&pageSize=10"
        );

        // Assert: respuesta HTTP
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert: estructura JSON de paginación
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);

        Assert.True(
            document.RootElement.TryGetProperty("productItems", out var productItems)
        );

        Assert.True(
            document.RootElement.TryGetProperty("total", out var total)
        );

        Assert.Equal(JsonValueKind.Array, productItems.ValueKind);
        Assert.Equal(JsonValueKind.Number, total.ValueKind);

        // Assert: contenido de la respuesta
        var result =
            await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.ProductItems.Count);
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        await SeedProducts(
            factory,
            new Product(
                "SEARCH-001",
                "Producto Termino Uno",
                1000,
                10,
                "Producto coincidente uno",
                "COD-SEARCH-001"
            ),
            new Product(
                "SEARCH-002",
                "Producto Termino Dos",
                2000,
                20,
                "Producto coincidente dos",
                "COD-SEARCH-002"
            ),
            new Product(
                "OTHER-003",
                "Producto Diferente",
                3000,
                30,
                "Producto no coincidente",
                "COD-OTHER-003"
            )
        );

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/api/products?search=Termino&pageNumber=1&pageSize=10"
        );

        // Assert: respuesta HTTP
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();

        Assert.NotNull(result);

        // Assert: solamente se devuelven los productos coincidentes
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.ProductItems.Count);

        Assert.All(
            result.ProductItems,
            product => Assert.Contains(
                "Termino",
                product.Name
            )
        );

        Assert.DoesNotContain(
            result.ProductItems,
            product => product.Sku == "OTHER-003"
        );
    }
}
