using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Enums;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dsw2025Tpi.Application.Services;

public class OrdersManagementService
{
    private readonly IRepository _repository;
    private readonly ILogger<OrdersManagementService> _logger;

    public OrdersManagementService(IRepository repository, ILogger<OrdersManagementService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    // --- GET ORDERS (CORREGIDO PARA PAGINACIÓN) ---
    public async Task<OrderResponsePagination> GetOrders(OrderFilter filter)
    {
        _logger.LogInformation("Obteniendo órdenes con filtros: {@Filter}", filter);

        // NOTA: Si tu repositorio no trae los items automáticamente, acá deberías usar un método 
        // que haga .Include(o => o.OrderItems).ThenInclude(i => i.Product)
        var query = await _repository.GetFiltered<Order>(
            o => true,                  // Condición (Traer todo)
            "Customer",                 // <--- IMPORTANTE: Para que aparezca el nombre del cliente
            "OrderItems",               // <--- IMPORTANTE: Para que la lista de items no esté vacía
            "OrderItems.Product"        // <--- IMPORTANTE: Para saber el nombre del producto dentro del item
        );

        // 1. Filtros
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.ToLower();
            query = query.Where(o =>
                (o.Customer != null && (o.Customer.Name.ToLower().Contains(term) || o.Customer.Email.ToLower().Contains(term)))
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("Todos", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<OrderStatus>(filter.Status, true, out var statusEnum))
            {
                query = query.Where(o => o.Status == statusEnum);
            }
        }

        // 2. Total y Paginación
        int totalItems = query.Count();
        int page = filter.PageNumber ?? 1;
        int size = filter.PageSize ?? 10;

        var pagedItems = query
            .OrderByDescending(o => o.Date)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => new OrderResponse(
                o.Id,
                o.Date,
                o.ShippingAddress,
                o.BillingAddress,
                o.Notes,
                o.TotalAmount,
                o.Status,
                // PROTECCIÓN CONTRA NULOS AQUÍ:
                (o.OrderItems != null)
                    ? o.OrderItems.Select(i => new OrderItemResponse(
                        i.ProductId ?? Guid.Empty,
                        i.Product != null ? i.Product.Name : "(Producto)",
                        i.Quantity,
                        i.UnitPrice,
                        i.Subtotal
                      )).ToList()
                    : new List<OrderItemResponse>(),
                o.Customer != null ? o.Customer.Name : "Cliente Desconocido"
            ))
            .ToList();

        return new OrderResponsePagination(pagedItems, totalItems);
    }

    // --- GET BY ID ---
    public async Task<OrderResponse> GetOrderById(Guid id)
    {
        _logger.LogInformation("Buscando orden con ID: {OrderID}", id);
        var order = await _repository.GetById<Order>(id, "OrderItems.Product", "Customer");

        if (order == null) throw new OrderNotFoundException(id);

        var orderItemResponses = order.OrderItems?.Select(oi => new OrderItemResponse(
            oi.ProductId ?? Guid.Empty,
            oi.Product?.Name ?? "(Producto eliminado)",
            oi.Quantity,
            oi.UnitPrice,
            oi.Subtotal
        )).ToList() ?? new List<OrderItemResponse>();

        return new OrderResponse(
            order.Id,
            order.Date,
            order.ShippingAddress,
            order.BillingAddress,
            order.Notes,
            order.TotalAmount,
            order.Status,
            orderItemResponses,
            order.Customer?.Name ?? "Cliente Desconocido"
        );
    }

    // --- CREATE ORDER ---
    public async Task<OrderResponse> CreateOrder(OrderRequest request)
    {
        _logger.LogInformation("Creando orden para cliente ID: {CustomerId}", request.CustomerId);

        if (request.Items == null || !request.Items.Any())
            throw new InvalidOrderDataException("La orden debe tener al menos un ítem.");

        var customer = await _repository.GetById<Customer>(request.CustomerId);
        if (customer == null) throw new CustomerNotFoundException(request.CustomerId);

        var orderItems = new List<OrderItem>();
        var reservations = new Dictionary<Guid, (Product Product, int Quantity)>();
        decimal total = 0m;

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0) throw new InvalidOrderDataException($"Cantidad inválida para producto {item.ProductId}");

            var product = reservations.TryGetValue(item.ProductId, out var reserved)
                ? reserved.Product
                : await _repository.GetById<Product>(item.ProductId);
            if (product == null || !product.IsActive) throw new ArgumentException($"Producto {item.ProductId} no disponible.");
            var quantity = checked(reserved.Quantity + item.Quantity);
            if (product.StockQuantity < quantity) throw new InsufficientStockException(product.Name, product.StockQuantity, quantity);
            reservations[item.ProductId] = (product, quantity);

            var orderItem = new OrderItem(Guid.Empty, product.Id, item.Quantity, product.CurrentUnitPrice);
            orderItem.Product = product; // Asignamos para tener el nombre en la respuesta inmediata

            orderItems.Add(orderItem);
            total += orderItem.Subtotal;
        }

        var order = new Order(request.CustomerId, request.ShippingAddress, request.BillingAddress, request.Notes)
        {
            TotalAmount = total,
            OrderItems = orderItems
        };

        // Validar todos los ítems antes de modificar stock; confirmar inventario y orden juntos.
        await _repository.ExecuteInTransaction(async () =>
        {
            foreach (var reservation in reservations.Values)
            {
                reservation.Product.StockQuantity -= reservation.Quantity;
                await _repository.Update(reservation.Product);
            }
            await _repository.Add(order);
        });

        return new OrderResponse(
            order.Id,
            order.Date,
            order.ShippingAddress,
            order.BillingAddress,
            order.Notes,
            order.TotalAmount,
            order.Status,
            orderItems.Select(oi => new OrderItemResponse(
                oi.ProductId ?? Guid.Empty,
                oi.Product?.Name ?? "(Producto)",
                oi.Quantity,
                oi.UnitPrice,
                oi.Subtotal
            )).ToList(),
            customer.Name
        );
    }

    // --- UPDATE STATUS ---
    public async Task<OrderResponse> UpdateOrderStatus(Guid orderId, string newStatus)
    {
        var order = await _repository.GetById<Order>(orderId, "OrderItems.Product", "Customer");
        if (order == null) throw new OrderNotFoundException(orderId);

        if (!Enum.TryParse<OrderStatus>(newStatus, true, out var parsedStatus))
            throw new InvalidOrderStatusException($"Estado '{newStatus}' no válido.");

        order.Status = parsedStatus;
        await _repository.Update(order);

        return new OrderResponse(
            order.Id,
            order.Date,
            order.ShippingAddress,
            order.BillingAddress,
            order.Notes,
            order.TotalAmount,
            order.Status,
            order.OrderItems?.Select(oi => new OrderItemResponse(
                oi.ProductId ?? Guid.Empty,
                oi.Product?.Name ?? "(Producto)",
                oi.Quantity,
                oi.UnitPrice,
                oi.Subtotal
            )).ToList() ?? new List<OrderItemResponse>(),
            order.Customer?.Name ?? "Cliente Desconocido"
        );
    }
}
