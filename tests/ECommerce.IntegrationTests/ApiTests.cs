using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class ApiTests(CustomWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private static ProductModel.Request ValidProduct(string sku = "SKU-1") => new(sku, "INT-1", "Mouse Gamer", "Óptico", 12.50m, 10);
    private static Product Product(string sku = "SKU-1", string name = "Mouse Gamer", bool active = true) => new(sku, name, 12.50m, 10, internalCode: "INT-1") { IsActive = active };
    private static Customer Customer() => new() { Name = "Cliente", Email = "user@example.test" };
    private static OrderRequest Order(Customer customer, Product product, int quantity = 3) => new(customer.Id, "Envío 123", "Factura 123", "TP4", [new(product.Id, quantity)]);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden(string? username, HttpStatusCode expected)
    {
        // Arrange
        using var client = username is null ? factory.CreateClient() : await factory.AuthenticatedClientAsync(username);
        // Act
        var response = await client.PostAsJsonAsync("/api/products", ValidProduct());
        // Assert
        response.StatusCode.Should().Be(expected);
        (await factory.ReadAsync(db => db.Set<Product>().CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // Arrange
        using var client = await factory.AuthenticatedClientAsync();
        var request = ValidProduct();
        // Act
        var response = await client.PostAsJsonAsync("/api/products", request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var body = (await response.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>())!;
        var saved = await factory.ReadAsync(db => db.Set<Product>().AsNoTracking().SingleAsync());
        saved.Should().BeEquivalentTo(request);
        saved.Id.Should().Be(body.Id);
        (await client.GetAsync(response.Headers.Location)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // Arrange
        await factory.SeedAsync(Product());
        using var client = await factory.AuthenticatedClientAsync();
        // Act
        var response = await client.PostAsJsonAsync("/api/products", ValidProduct());
        // Assert: la consigna admite 400 o 409; se conserva el contrato existente 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SKU-1");
        (await factory.ReadAsync(db => db.Set<Product>().CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        using var client = factory.CreateClient();
        // Act
        var response = await client.PostAsJsonAsync("/api/orders", Order(Customer(), Product()));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        // Arrange
        var customer = Customer(); var product = Product();
        await factory.SeedAsync(customer, product);
        using var client = await factory.AuthenticatedClientAsync("user");
        // Act
        var response = await client.PostAsJsonAsync("/api/orders", Order(customer, product));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        var id = body.GetProperty("orderId").GetGuid();
        var saved = await factory.ReadAsync(db => db.Set<Order>().AsNoTracking().Include(o => o.OrderItems).SingleAsync());
        saved.Id.Should().Be(id); saved.TotalAmount.Should().Be(37.50m);
        saved.CustomerId.Should().Be(customer.Id);
        saved.OrderItems.Should().ContainSingle().Which.Quantity.Should().Be(3);
        (await factory.ReadAsync(db => db.Set<Product>().AsNoTracking().SingleAsync())).StockQuantity.Should().Be(7);
        (await client.GetAsync(response.Headers.Location)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        // Arrange
        var customer = Customer(); var product = Product();
        await factory.SeedAsync(customer, product);
        using var client = await factory.AuthenticatedClientAsync("user");
        // Act
        var response = await client.PostAsJsonAsync("/api/orders", Order(customer, product, 20));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).ToLowerInvariant().Should().Contain("stock");
        (await factory.ReadAsync(db => db.Set<Order>().CountAsync())).Should().Be(0);
        (await factory.ReadAsync(db => db.Set<Product>().SingleAsync())).StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // Arrange
        await factory.SeedAsync(Product());
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync("/api/products");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("productItems").GetArrayLength().Should().Be(1);
        body.GetProperty("totalItems").GetInt32().Should().Be(1);
        body.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // Arrange
        await factory.SeedAsync(Product("A", "Mouse Gamer"), Product("B", "Teclado Mecánico"), Product("C", "Monitor"));
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync("/api/products?search=mOuSe&pageNumber=1&pageSize=10");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>())!;
        body.ProductItems.Should().ContainSingle().Which.Name.Should().Be("Mouse Gamer");
        body.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // Arrange
        await factory.SeedAsync(Product("A"), Product("B", "Inactivo", false));
        using var admin = await factory.AuthenticatedClientAsync();
        using var anonymous = factory.CreateClient();
        // Act
        var response = await admin.GetAsync("/api/products/admin");
        var publicResponse = await anonymous.GetAsync("/api/products");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var all = (await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>())!;
        all.ProductItems.Should().HaveCount(2).And.Contain(p => !p.IsActive);
        var active = (await publicResponse.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>())!;
        active.ProductItems.Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetProducts_WhenDatabaseIsEmpty_ReturnsNoContent()
    {
        // Arrange
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync("/api/products");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedJwt_ReturnsUnauthorized()
    {
        // Arrange
        using var client = await factory.AuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.signature");
        // Act
        var response = await client.PostAsJsonAsync("/api/products", ValidProduct());
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOrder_WhenLaterItemFails_DoesNotPersistPartialStockChanges()
    {
        // Arrange
        var customer = Customer(); var first = Product("A"); var second = Product("B");
        await factory.SeedAsync(customer, first, second);
        using var client = await factory.AuthenticatedClientAsync("user");
        var request = Order(customer, first) with { Items = [new(first.Id, 3), new(second.Id, 99)] };
        // Act
        var response = await client.PostAsJsonAsync("/api/orders", request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stocks = await factory.ReadAsync(db => db.Set<Product>().AsNoTracking().Select(p => p.StockQuantity).ToArrayAsync());
        stocks.Should().Equal(10, 10);
        (await factory.ReadAsync(db => db.Set<Order>().CountAsync())).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task E2E_LoginCreateProductPurchaseAndReadOrder_PreservesInventoryAndTotals()
    {
        // Arrange: cliente; autenticación y producto se obtienen por HTTP.
        var customer = Customer(); await factory.SeedAsync(customer);
        using var admin = await factory.AuthenticatedClientAsync();
        using var user = await factory.AuthenticatedClientAsync("user");
        // Act: login -> alta de producto -> catálogo -> compra -> consulta de orden y stock.
        var createdProduct = await admin.PostAsJsonAsync("/api/products", ValidProduct("E2E-1"));
        createdProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await createdProduct.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>())!;
        var catalog = await user.GetFromJsonAsync<ProductModel.ResponsePagination>("/api/products?search=e2e-1");
        catalog!.ProductItems.Should().ContainSingle().Which.Id.Should().Be(product.Id);
        var createdOrder = await user.PostAsJsonAsync("/api/orders", new OrderRequest(customer.Id, "Envío", "Factura", null, [new(product.Id, 3)]));
        createdOrder.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await user.GetFromJsonAsync<JsonElement>(createdOrder.Headers.Location);
        var remaining = await user.GetFromJsonAsync<ProductModel.ResponseProduct>($"/api/products/{product.Id}");
        // Assert
        order.GetProperty("totalAmount").GetDecimal().Should().Be(37.50m);
        order.GetProperty("items")[0].GetProperty("quantity").GetInt32().Should().Be(3);
        remaining!.StockQuantity.Should().Be(7);
        (await factory.ReadAsync(db => db.Set<Order>().CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Repository_WhenOrderInsertFails_RollsBackPreviouslySavedStock()
    {
        // Arrange: la FK del cliente inexistente fuerza un fallo de persistencia real.
        await factory.SeedAsync(Product());
        // Act
        await factory.ReadAsync(async db =>
        {
            var repository = new Dsw2025Tpi.Data.Repositories.EfRepository(db);
            Func<Task> act = () => repository.ExecuteInTransaction(async () =>
            {
                var product = await db.Set<Product>().SingleAsync();
                product.StockQuantity = 7;
                await repository.Update(product);
                await repository.Add(new Order(Guid.NewGuid(), "Envío", "Factura"));
            });
            await act.Should().ThrowAsync<DbUpdateException>();
            return true;
        });
        // Assert: nuevo scope, sin reutilizar la entidad en memoria.
        (await factory.ReadAsync(db => db.Set<Product>().AsNoTracking().SingleAsync())).StockQuantity.Should().Be(10);
        (await factory.ReadAsync(db => db.Set<Order>().CountAsync())).Should().Be(0);
    }
}
