
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class ProductsListingServiceTests
{
    [Fact]
    public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var activeProduct1 = new Product(
            "SKU-ACTIVE-001",
            "Teclado",
            1500m,
            10,
            "Teclado mecanico",
            "INT-ACTIVE-001");

        var activeProduct2 = new Product(
            "SKU-ACTIVE-002",
            "Mouse",
            2500m,
            5,
            "Mouse inalambrico",
            "INT-ACTIVE-002");

        var inactiveProduct = new Product(
            "SKU-INACTIVE-001",
            "Monitor",
            3500m,
            3,
            "Monitor LED",
            "INT-INACTIVE-001");

        inactiveProduct.IsActive = false;

        var products = new List<Product>
        {
            activeProduct1,
            activeProduct2,
            inactiveProduct
        };

        var filter = new ProductModel.FilterProduct(
            Status: null,
            Search: null,
            PageNumber: null,
            PageSize: null);

        repositoryMock
            .Setup(repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((
                Expression<Func<Product, bool>> predicate,
                string[] includes) =>
                products.Where(predicate.Compile()).ToList());

        // ACT

        var result = await service.GetProducts(
            filter,
            includeInactive: false);

        // ASSERT

        result.Should().NotBeNull();
        result.Total.Should().Be(2);
        result.ProductItems.Should().HaveCount(2);

        result.ProductItems.Should().OnlyContain(
            product => product.IsActive);

        result.ProductItems.Select(product => product.Sku)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    "SKU-ACTIVE-001",
                    "SKU-ACTIVE-002"
                });

        repositoryMock.Verify(
            repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var products = new List<Product>
        {
            new Product("SKU-001", "Auriculares", 1000m, 10),
            new Product("SKU-002", "Camara", 2000m, 10),
            new Product("SKU-003", "Monitor", 3000m, 10),
            new Product("SKU-004", "Mouse", 4000m, 10),
            new Product("SKU-005", "Teclado", 5000m, 10)
        };

        var filter = new ProductModel.FilterProduct(
            Status: null,
            Search: null,
            PageNumber: 2,
            PageSize: 2);

        repositoryMock
            .Setup(repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((
                Expression<Func<Product, bool>> predicate,
                string[] includes) =>
                products.Where(predicate.Compile()).ToList());

        // ACT

        var result = await service.GetProducts(
            filter,
            includeInactive: false);

        // ASSERT

        result.Should().NotBeNull();
        result.Total.Should().Be(5);
        result.ProductItems.Should().HaveCount(2);

        result.ProductItems.Select(product => product.Name)
            .Should()
            .Equal("Monitor", "Mouse");

        repositoryMock.Verify(
            repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Theory]
    [InlineData("Monitor", "SKU-SEARCH-001")]
    [InlineData("SKU-SEARCH-002", "SKU-SEARCH-002")]
    [InlineData("INT-SEARCH-003", "SKU-SEARCH-003")]
    public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode(
        string searchTerm,
        string expectedSku)
    {
        // ARRANGE

        var repositoryMock = new Mock<IRepository>();
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();

        var service = new ProductsManagementService(
            repositoryMock.Object,
            loggerMock.Object);

        var products = new List<Product>
        {
            new Product(
                "SKU-SEARCH-001",
                "Monitor",
                3000m,
                10,
                "Monitor LED",
                "INT-SEARCH-001"),

            new Product(
                "SKU-SEARCH-002",
                "Teclado",
                1500m,
                10,
                "Teclado mecanico",
                "INT-SEARCH-002"),

            new Product(
                "SKU-SEARCH-003",
                "Mouse",
                2000m,
                10,
                "Mouse inalambrico",
                "INT-SEARCH-003")
        };

        var filter = new ProductModel.FilterProduct(
            Status: null,
            Search: searchTerm,
            PageNumber: null,
            PageSize: null);

        repositoryMock
            .Setup(repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((
                Expression<Func<Product, bool>> predicate,
                string[] includes) =>
                products.Where(predicate.Compile()).ToList());

        // ACT

        var result = await service.GetProducts(
            filter,
            includeInactive: false);

        // ASSERT

        result.Should().NotBeNull();
        result.Total.Should().Be(1);
        result.ProductItems.Should().ContainSingle();

        result.ProductItems[0].Sku.Should().Be(expectedSku);

        repositoryMock.Verify(
            repository => repository.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()),
            Times.Once);
    }
}
