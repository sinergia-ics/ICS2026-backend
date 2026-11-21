using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ECommerce.UnitTests;

public class ProductsManagementServiceTests
{
    private readonly Mock<IRepository> _repository = new(MockBehavior.Strict);
    private ProductsManagementService Service => new(_repository.Object, NullLogger<ProductsManagementService>.Instance);
    private static ProductModel.Request ValidRequest => new("SKU-1", "INT-1", "Mouse Gamer", "Óptico", 125.50m, 10);

    [Fact]
    public async Task AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd()
    {
        // Arrange
        var request = ValidRequest;
        _repository.Setup(r => r.First(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>())).ReturnsAsync((Product?)null);
        _repository.Setup(r => r.Add(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
        // Act
        var result = await Service.AddProduct(request);
        // Assert
        result.Should().BeEquivalentTo(request);
        result.Id.Should().NotBeEmpty();
        result.IsActive.Should().BeTrue();
        _repository.Verify(r => r.Add(It.Is<Product>(p => p.Id == result.Id && p.Sku == request.Sku)), Times.Once);
    }

    [Fact]
    public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException()
    {
        // Arrange
        var existing = new Product("SKU-1", "Existente", 5, 1);
        _repository.Setup(r => r.First(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>())).ReturnsAsync(existing);
        // Act
        var act = () => Service.AddProduct(ValidRequest);
        // Assert
        await act.Should().ThrowAsync<DuplicatedEntityException>().WithMessage("*SKU-1*");
        _repository.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, -0.01)]
    public async Task AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException(int stock, decimal price)
    {
        // Arrange
        var request = ValidRequest with { StockQuantity = stock, CurrentUnitPrice = price };
        // Act
        var act = () => Service.AddProduct(request);
        // Assert
        (await act.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().NotBeNullOrWhiteSpace();
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddProduct_WithZeroPrice_AcceptsBoundaryValue()
    {
        // Arrange: el enunciado prohíbe precio negativo, admite cero.
        _repository.Setup(r => r.First(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>())).ReturnsAsync((Product?)null);
        _repository.Setup(r => r.Add(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
        // Act
        var result = await Service.AddProduct(ValidRequest with { CurrentUnitPrice = 0 });
        // Assert
        result.CurrentUnitPrice.Should().Be(0);
    }

    [Theory]
    [InlineData("", "Mouse")]
    [InlineData("SKU", " ")]
    public async Task AddProduct_WithMissingNameOrSku_DoesNotAccessRepository(string sku, string name)
    {
        // Arrange
        var request = ValidRequest with { Sku = sku, Name = name };
        // Act
        var act = () => Service.AddProduct(request);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
        _repository.VerifyNoOtherCalls();
    }

    private void Seed(params Product[] products) => _repository
        .Setup(r => r.GetFiltered(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
        .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] _) => products.Where(predicate.Compile()).ToArray());

    [Fact]
    public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
    {
        // Arrange: el mock aplica la expresión, como lo haría EF Core.
        var active = new Product("A", "Activo", 10, 1);
        Seed(active, new Product("B", "Inactivo", 20, 1) { IsActive = false });
        // Act
        var result = await Service.GetProducts(new(null, null, null, null));
        // Assert
        result.ProductItems.Should().ContainSingle().Which.Id.Should().Be(active.Id);
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
    {
        // Arrange
        Seed(Enumerable.Range(1, 15).Select(i => new Product($"SKU-{i:00}", $"Producto {i:00}", i, 1)).ToArray());
        // Act
        var result = await Service.GetProducts(new(null, null, 2, 5));
        // Assert
        result.Total.Should().Be(15);
        result.ProductItems.Select(p => p.Sku).Should().Equal("SKU-06", "SKU-07", "SKU-08", "SKU-09", "SKU-10");
    }

    [Theory]
    [InlineData("mouse", "Mouse Gamer", "A", "X")]
    [InlineData("MoUsE", "Teclado", "SKU-MOUSE", "X")]
    [InlineData("MOUSE", "Monitor", "A", "INT-Mouse")]
    public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode(string search, string name, string sku, string code)
    {
        // Arrange
        var match = new Product(sku, name, 1, 1, internalCode: code);
        Seed(match, new Product("OTRO", "Sin coincidencia", 1, 1));
        // Act
        var result = await Service.GetProducts(new(null, search, 1, 10));
        // Assert
        result.ProductItems.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 10)]
    public async Task GetProducts_WithInvalidPagination_RejectsBeforeRepository(int page, int size)
    {
        // Arrange
        var filter = new ProductModel.FilterProduct(null, null, page, size);
        // Act
        var act = () => Service.GetProducts(filter);
        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
        _repository.VerifyNoOtherCalls();
    }
}
