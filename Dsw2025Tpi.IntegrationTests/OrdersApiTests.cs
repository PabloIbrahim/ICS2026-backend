
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

public class OrdersApiTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<(Customer Customer, Product Product)>
        SeedCustomerAndProduct(
            CustomWebApplicationFactory factory,
            int stockQuantity)
    {
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        var customer = new Customer
        {
            Name = "Cliente de integración",
            Email = "cliente.integration@test.com",
            PhoneNumber = "3815551234"
        };

        var product = new Product(
            "ORDER-001",
            "Producto para orden",
            1000,
            stockQuantity,
            "Producto utilizado en pruebas de integración",
            "COD-ORDER-001"
        );

        await context.Set<Customer>().AddAsync(customer);
        await context.Set<Product>().AddAsync(product);

        await context.SaveChangesAsync();

        return (customer, product);
    }

    private static OrderRequest CreateOrderRequest(
        Guid customerId,
        Guid productId,
        int quantity)
    {
        return new OrderRequest(
            CustomerId: customerId,
            ShippingAddress: "Dirección de envío de prueba",
            BillingAddress: "Dirección de facturación de prueba",
            Notes: "Orden generada durante una prueba de integración",
            Items: new List<OrderItemRequest>
            {
                new OrderItemRequest(productId, quantity)
            }
        );
    }

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // ARRANGE
        using var factory = new CustomWebApplicationFactory();

        factory.InitializeDatabase();

        using var client = factory.CreateClient();

        var request = CreateOrderRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1
        );

        // ACT
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request
        );

        // ASSERT
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }

    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        // ARRANGE
        using var factory = new CustomWebApplicationFactory();

        factory.InitializeDatabase();

        var (customer, product) =
            await SeedCustomerAndProduct(factory, stockQuantity: 10);

        using var client = factory.CreateClient();

        TestAuthenticationHelper.AuthenticateAsUser(
            client,
            factory.Services,
            customer.Id.ToString()
        );

        var request = CreateOrderRequest(
            customer.Id,
            product.Id,
            quantity: 3
        );

        // ACT
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request
        );

        // ASSERT - Respuesta HTTP
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        Assert.NotNull(response.Headers.Location);

        var responseBody = await response.Content.ReadAsStringAsync();

        var createdOrder = JsonSerializer.Deserialize<OrderResponse>(
            responseBody,
            ApiJsonOptions
        );

        Assert.NotNull(createdOrder);

        Assert.NotEqual(Guid.Empty, createdOrder.OrderId);
        Assert.Equal(customer.Name, createdOrder.ClientName);
        Assert.Equal(3000m, createdOrder.TotalAmount);
        Assert.Single(createdOrder.Items);

        Assert.Equal(product.Id, createdOrder.Items[0].ProductId);
        Assert.Equal(3, createdOrder.Items[0].Quantity);
        Assert.Equal(1000m, createdOrder.Items[0].UnitPrice);
        Assert.Equal(3000m, createdOrder.Items[0].Subtotal);

        // ASSERT - Persistencia de la orden en SQLite
        using var verificationScope = factory.Services.CreateScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        var persistedOrder = await verificationContext.Set<Order>()
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(o => o.Id == createdOrder.OrderId);

        Assert.NotNull(persistedOrder);

        Assert.Equal(customer.Id, persistedOrder.CustomerId);
        Assert.Equal(3000m, persistedOrder.TotalAmount);
        Assert.Single(persistedOrder.OrderItems!);

        var persistedItem = persistedOrder.OrderItems!.Single();

        Assert.Equal(product.Id, persistedItem.ProductId);
        Assert.Equal(3, persistedItem.Quantity);
        Assert.Equal(1000m, persistedItem.UnitPrice);

        // ASSERT - Persistencia de la actualización del stock
        var persistedProduct = await verificationContext.Set<Product>()
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(7, persistedProduct.StockQuantity);
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        // ARRANGE
        using var factory = new CustomWebApplicationFactory();

        factory.InitializeDatabase();

        var (customer, product) =
            await SeedCustomerAndProduct(factory, stockQuantity: 2);

        using var client = factory.CreateClient();

        TestAuthenticationHelper.AuthenticateAsUser(
            client,
            factory.Services,
            customer.Id.ToString()
        );

        var request = CreateOrderRequest(
            customer.Id,
            product.Id,
            quantity: 3
        );

        // ACT
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request
        );

        // ASSERT - Respuesta HTTP
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var responseBody =
            await response.Content.ReadAsStringAsync();

        Assert.False(string.IsNullOrWhiteSpace(responseBody));

        using var jsonDocument = JsonDocument.Parse(responseBody);

        var root = jsonDocument.RootElement;

        Assert.True(
            root.TryGetProperty("Message", out var messageProperty),
            $"No se encontró la propiedad Message. Respuesta recibida: {responseBody}"
        );

        var errorMessage = messageProperty.GetString();

        Assert.NotNull(errorMessage);

        Assert.Contains("Producto para orden", errorMessage);
        Assert.Contains("Stock actual: 2", errorMessage);
        Assert.Contains("solicitado: 3", errorMessage);

        // ASSERT - El stock no debe modificarse
        using var verificationScope = factory.Services.CreateScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<Dsw2025TpiContext>();

        var persistedProduct = await verificationContext.Set<Product>()
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(2, persistedProduct.StockQuantity);

        // ASSERT - No debe haberse creado ninguna orden
        var ordersCount = await verificationContext.Set<Order>()
            .CountAsync();

        Assert.Equal(0, ordersCount);
    }
}
