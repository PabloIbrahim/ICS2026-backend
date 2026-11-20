
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class ProductsManagementServiceTests
{
    [Fact]
    public async Task AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd()
    {
        // ARRANGE
        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var request = new ProductModel.Request(
            "SKU-001",
            "INT-001",
            "Teclado",
            "Teclado mecanico",
            1500,
            10);

        repositoryMock
            .Setup(repository => repository.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);

        repositoryMock
            .Setup(repository => repository.Add(It.IsAny<Product>()))
            .ReturnsAsync((Product product) => product);

        // ACT
        var result = await service.AddProduct(request);

        // ASSERT
        result.Should().NotBeNull();
        result.Sku.Should().Be("SKU-001");
        result.InternalCode.Should().Be("INT-001");
        result.Name.Should().Be("Teclado");
        result.Description.Should().Be("Teclado mecanico");
        result.CurrentUnitPrice.Should().Be(1500);
        result.StockQuantity.Should().Be(10);
        result.IsActive.Should().BeTrue();

        repositoryMock.Verify(
            repository => repository.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.Add(It.Is<Product>(
                product => product.Sku == "SKU-001"
                    && product.Name == "Teclado")),
            Times.Once);
    }

    [Fact]
    public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException()
    {
        // ARRANGE
        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var request = new ProductModel.Request(
            "SKU-001",
            "INT-002",
            "Mouse",
            "Mouse inalambrico",
            2500,
            5);

        var existingProduct = new Product(
            "SKU-001",
            "Teclado",
            1500,
            10);

        repositoryMock
            .Setup(repository => repository.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync(existingProduct);

        // ACT
        Func<Task> act = async () => await service.AddProduct(request);

        // ASSERT
        await act.Should().ThrowAsync<DuplicatedEntityException>();

        repositoryMock.Verify(
            repository => repository.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Once);

        repositoryMock.Verify(
            repository => repository.Add(It.IsAny<Product>()),
            Times.Never);
    }

    [Theory]
    [InlineData(-100, 10)]
    [InlineData(1500, 0)]
    public async Task AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException(
        decimal price,
        int stock)
    {
        // ARRANGE
        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var request = new ProductModel.Request(
            "SKU-003",
            "INT-003",
            "Monitor",
            "Monitor LED",
            price,
            stock);

        // ACT
        Func<Task> act = async () => await service.AddProduct(request);

        // ASSERT
        await act.Should().ThrowAsync<InvalidDataException>();

        repositoryMock.Verify(
            repository => repository.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Never);

        repositoryMock.Verify(
            repository => repository.Add(It.IsAny<Product>()),
            Times.Never);
    }
}
