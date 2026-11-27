
using System.Net;
using System.Net.Http.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

public class ProductsAdminApiTests
{
    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.InitializeDatabase();

        var activeProduct = new Product(
            "ADMIN-ACTIVE-001",
            "Producto Activo",
            1000,
            10,
            "Producto activo para integración",
            "COD-ADMIN-001"
        );

        var inactiveProduct = new Product(
            "ADMIN-INACTIVE-002",
            "Producto Inactivo",
            2000,
            5,
            "Producto inactivo para integración",
            "COD-ADMIN-002"
        );

        // Deshabilitamos el segundo producto.
        inactiveProduct.IsActive = false;

        // Insertamos ambos productos en SQLite.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<Dsw2025TpiContext>();

            await context.Set<Product>().AddRangeAsync(
                activeProduct,
                inactiveProduct
            );

            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient();

        // Autenticamos al cliente con un token de administrador.
        TestAuthenticationHelper.AuthenticateAsAdmin(
            client,
            factory.Services
        );

        // Act
        var response = await client.GetAsync(
            "/api/products/admin?pageNumber=1&pageSize=10"
        );

        // Assert: respuesta HTTP
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();

        Assert.NotNull(result);

        // Assert: se devuelven ambos productos.
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.ProductItems.Count);

        // Assert: el producto inactivo está incluido.
        var returnedInactiveProduct = result.ProductItems
            .SingleOrDefault(p => p.Sku == "ADMIN-INACTIVE-002");

        Assert.NotNull(returnedInactiveProduct);
        Assert.False(returnedInactiveProduct.IsActive);

        // Assert: el producto activo también está incluido.
        var returnedActiveProduct = result.ProductItems
            .SingleOrDefault(p => p.Sku == "ADMIN-ACTIVE-001");

        Assert.NotNull(returnedActiveProduct);
        Assert.True(returnedActiveProduct.IsActive);
    }
}
