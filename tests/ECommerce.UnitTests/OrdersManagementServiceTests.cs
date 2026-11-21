using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ECommerce.UnitTests;

public class OrdersManagementServiceTests
{
    private readonly Mock<IRepository> _repository = new(MockBehavior.Strict);
    private readonly Customer _customer = new() { Name = "Cliente", Email = "cliente@example.test" };
    private OrdersManagementService Service => new(_repository.Object, NullLogger<OrdersManagementService>.Instance);
    private OrderRequest Request(params OrderItemRequest[] items) => new(_customer.Id, "Envío 123", "Factura 123", null, items.ToList());
    private void ExistingCustomer() => _repository.Setup(r => r.GetById<Customer>(_customer.Id, It.IsAny<string[]>())).ReturnsAsync(_customer);
    private void ExistingProduct(Product product) => _repository.Setup(r => r.GetById<Product>(product.Id, It.IsAny<string[]>())).ReturnsAsync(product);

    [Fact]
    public async Task CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock()
    {
        // Arrange
        ExistingCustomer();
        var first = new Product("A", "Mouse", 10.25m, 10);
        var second = new Product("B", "Teclado", 20.50m, 8);
        ExistingProduct(first); ExistingProduct(second);
        _repository.Setup(r => r.Update(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
        _repository.Setup(r => r.Add(It.IsAny<Order>())).ReturnsAsync((Order o) => o);
        _repository.Setup(r => r.ExecuteInTransaction(It.IsAny<Func<Task>>())).Returns((Func<Task> operation) => operation());
        // Act
        var result = await Service.CreateOrder(Request(new OrderItemRequest(first.Id, 3), new(second.Id, 2)));
        // Assert
        result.TotalAmount.Should().Be(71.75m);
        result.Items.Sum(i => i.Subtotal).Should().Be(result.TotalAmount);
        first.StockQuantity.Should().Be(7); second.StockQuantity.Should().Be(6);
        _repository.Verify(r => r.Update(first), Times.Once);
        _repository.Verify(r => r.Update(second), Times.Once);
        _repository.Verify(r => r.Add(It.Is<Order>(o => o.TotalAmount == 71.75m && o.CustomerId == _customer.Id)), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // Arrange
        ExistingCustomer();
        var product = new Product("A", "Mouse", 10, 2); ExistingProduct(product);
        // Act
        var act = () => Service.CreateOrder(Request(new OrderItemRequest(product.Id, 5)));
        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();
        product.StockQuantity.Should().Be(2);
        VerifyNoWrites();
    }

    [Fact]
    public async Task CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException()
    {
        // Arrange
        _repository.Setup(r => r.GetById<Customer>(_customer.Id, It.IsAny<string[]>())).ReturnsAsync((Customer?)null);
        // Act
        var act = () => Service.CreateOrder(Request(new OrderItemRequest(Guid.NewGuid(), 1)));
        // Assert
        await act.Should().ThrowAsync<CustomerNotFoundException>();
        _repository.Verify(r => r.GetById<Product>(It.IsAny<Guid>(), It.IsAny<string[]>()), Times.Never);
        VerifyNoWrites();
    }

    [Fact]
    public async Task CreateOrder_WhenLaterItemHasInsufficientStock_DoesNotChangeEarlierProduct()
    {
        // Arrange
        ExistingCustomer();
        var first = new Product("A", "Primero", 10, 10);
        var second = new Product("B", "Segundo", 10, 2);
        ExistingProduct(first); ExistingProduct(second);
        // Act
        var act = () => Service.CreateOrder(Request(new OrderItemRequest(first.Id, 3), new(second.Id, 5)));
        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();
        first.StockQuantity.Should().Be(10); second.StockQuantity.Should().Be(2);
        VerifyNoWrites();
    }

    [Fact]
    public async Task CreateOrder_WhenRepeatedProductExceedsCombinedStock_RejectsWithoutWrites()
    {
        // Arrange
        ExistingCustomer();
        var product = new Product("A", "Mouse", 10, 5); ExistingProduct(product);
        // Act
        var act = () => Service.CreateOrder(Request(new OrderItemRequest(product.Id, 3), new(product.Id, 3)));
        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();
        product.StockQuantity.Should().Be(5);
        VerifyNoWrites();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateOrder_WithInvalidQuantity_RejectsWithoutWrites(int quantity)
    {
        // Arrange
        ExistingCustomer();
        // Act
        var act = () => Service.CreateOrder(Request(new OrderItemRequest(Guid.NewGuid(), quantity)));
        // Assert
        await act.Should().ThrowAsync<InvalidOrderDataException>();
        VerifyNoWrites();
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_RejectsBeforeRepository()
    {
        // Arrange
        var request = Request();
        // Act
        var act = () => Service.CreateOrder(request);
        // Assert
        await act.Should().ThrowAsync<InvalidOrderDataException>();
        _repository.VerifyNoOtherCalls();
    }

    private void VerifyNoWrites()
    {
        _repository.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
        _repository.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        _repository.Verify(r => r.ExecuteInTransaction(It.IsAny<Func<Task>>()), Times.Never);
    }
}
