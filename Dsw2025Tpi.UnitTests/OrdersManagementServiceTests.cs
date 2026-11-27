
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Enums;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class OrdersManagementServiceTests
{
    [Fact]
    public async Task CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock()
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<OrdersManagementService>>();

        var service = new OrdersManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var customer = new Customer
        {
            Name = "Cliente de prueba",
            Email = "cliente@test.com",
            PhoneNumber = "3811234567"
        };

        var product = new Product(
            "SKU-ORDER-001",
            "Monitor",
            1500m,
            10,
            "Monitor LED",
            "INT-ORDER-001");

        var requestedQuantity = 2;

        var request = new global::OrderRequest(
            customer.Id,
            "Dirección de envío",
            "Dirección de facturación",
            "Orden de prueba",
            new List<OrderItemRequest>
            {
                new OrderItemRequest(product.Id, requestedQuantity)
            });

        repositoryMock
            .Setup(repository => repository.GetById<Customer>(
                customer.Id,
                It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        repositoryMock
            .Setup(repository => repository.GetById<Product>(
                product.Id,
                It.IsAny<string[]>()))
            .ReturnsAsync(product);

        repositoryMock
            .Setup(repository => repository.Update(It.IsAny<Product>()))
            .ReturnsAsync((Product updatedProduct) => updatedProduct);

        repositoryMock
            .Setup(repository => repository.Add(It.IsAny<Order>()))
            .ReturnsAsync((Order newOrder) => newOrder);

        // ACT

        var result = await service.CreateOrder(request);

        // ASSERT

        result.TotalAmount.Should().Be(3000m);
        result.ClientName.Should().Be("Cliente de prueba");

        result.Items.Should().HaveCount(1);
        result.Items[0].ProductId.Should().Be(product.Id);
        result.Items[0].ProductName.Should().Be("Monitor");
        result.Items[0].Quantity.Should().Be(2);
        result.Items[0].UnitPrice.Should().Be(1500m);
        result.Items[0].Subtotal.Should().Be(3000m);

        result.Status.Should().Be(OrderStatus.PENDING);

        product.StockQuantity.Should().Be(8);

        repositoryMock.Verify(
            repository => repository.Update(
                It.Is<Product>(p =>
                    p.Id == product.Id &&
                    p.StockQuantity == 8)),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.Add(
                It.Is<Order>(order =>
                    order.CustomerId == customer.Id &&
                    order.TotalAmount == 3000m &&
                    order.OrderItems != null &&
                    order.OrderItems.Count == 1)),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.GetById<Customer>(
                customer.Id,
                It.IsAny<string[]>()),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.GetById<Product>(
                product.Id,
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<OrdersManagementService>>();

        var service = new OrdersManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var customer = new Customer
        {
            Name = "Cliente de prueba",
            Email = "cliente@test.com"
        };

        var product = new Product(
            "SKU-ORDER-002",
            "Teclado",
            2000m,
            3,
            "Teclado mecánico",
            "INT-ORDER-002");

        var stockInicial = product.StockQuantity;
        var requestedQuantity = 5;

        var request = new global::OrderRequest(
            customer.Id,
            "Dirección de envío",
            "Dirección de facturación",
            "Orden con stock insuficiente",
            new List<OrderItemRequest>
            {
                new OrderItemRequest(product.Id, requestedQuantity)
            });

        repositoryMock
            .Setup(repository => repository.GetById<Customer>(
                customer.Id,
                It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        repositoryMock
            .Setup(repository => repository.GetById<Product>(
                product.Id,
                It.IsAny<string[]>()))
            .ReturnsAsync(product);

        // ACT

        Func<Task> act = async () => await service.CreateOrder(request);

        // ASSERT

        await act.Should()
            .ThrowAsync<InsufficientStockException>();

        product.StockQuantity.Should().Be(stockInicial);

        repositoryMock.Verify(
            repository => repository.Update(It.IsAny<Product>()),
            Times.Never);

        repositoryMock.Verify(
            repository => repository.Add(It.IsAny<Order>()),
            Times.Never);

        repositoryMock.Verify(
            repository => repository.GetById<Customer>(
                customer.Id,
                It.IsAny<string[]>()),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.GetById<Product>(
                product.Id,
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException()
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<OrdersManagementService>>();

        var service = new OrdersManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var customerId = Guid.NewGuid();

        var request = new global::OrderRequest(
            customerId,
            "Dirección de envío",
            "Dirección de facturación",
            "Orden sin cliente existente",
            new List<OrderItemRequest>
            {
                new OrderItemRequest(Guid.NewGuid(), 1)
            });

        repositoryMock
            .Setup(repository => repository.GetById<Customer>(
                customerId,
                It.IsAny<string[]>()))
            .ReturnsAsync((Customer?)null);

        // ACT

        Func<Task> act = async () => await service.CreateOrder(request);

        // ASSERT

        await act.Should()
            .ThrowAsync<CustomerNotFoundException>();

        repositoryMock.Verify(
            repository => repository.GetById<Customer>(
                customerId,
                It.IsAny<string[]>()),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.GetById<Product>(
                It.IsAny<Guid>(),
                It.IsAny<string[]>()),
            Times.Never);

        repositoryMock.Verify(
            repository => repository.Update(It.IsAny<Product>()),
            Times.Never);

        repositoryMock.Verify(
            repository => repository.Add(It.IsAny<Order>()),
            Times.Never);
    }
}
