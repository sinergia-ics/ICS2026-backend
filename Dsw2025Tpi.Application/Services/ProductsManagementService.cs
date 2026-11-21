using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Domain.Interfaces;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Application.Exceptions;
using Microsoft.Extensions.Logging;



namespace Dsw2025Tpi.Application.Services
{
    public class ProductsManagementService
    {
        private readonly IRepository _repository;
        private readonly ILogger<ProductsManagementService> _logger;
        public ProductsManagementService(IRepository repository, ILogger<ProductsManagementService> logger)
        {
            _repository = repository;
            _logger = logger;
        }


        public async Task<ProductModel.ResponseProduct> AddProduct(ProductModel.Request request)
        {
            _logger.LogInformation("Intentando agregar un nuevo producto con SKU: {Sku}", request.Sku);
            if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name))
            {
                _logger.LogError("Intento de crear producto sin nombre o sku");
                throw new InvalidDataException("El sku y el nombre son requerido");
            }
            if (request.CurrentUnitPrice < 0)
            {
                _logger.LogError("Intento crear un producto con precio unitario negativo: {precio}", request.CurrentUnitPrice);
                throw new InvalidDataException("El precio unitario ser mayores a cero");
            }
            if (request.StockQuantity <= 0)
            {
                _logger.LogError("Intento crear un producto con stock negativo: {stock}", request.StockQuantity);
                throw new InvalidDataException("La cantidad de stock debe ser mayor o igual a cero");
            }
            var exist = await _repository.First<Product>(p => p.Sku.Trim() == request.Sku.Trim());

            if (exist != null)
            {
                _logger.LogError("Intento de crear producto con SKU duplicado: {Sku}", request.Sku);
                throw new DuplicatedEntityException($"Ya existe un producto con el mismo SKU {request.Sku}");
            }
            var product = new Product(
                request.Sku,
                request.Name,
                request.CurrentUnitPrice,
                request.StockQuantity,
                request.Description,
                request.InternalCode);

            await _repository.Add(product);

            _logger.LogInformation("Producto con ID {ProductId} y SKU {Sku} creado exitosamente.", product.Id, product.Sku);
            return new ProductModel.ResponseProduct(
                product.Id,
                product.Sku,
                product.InternalCode,
                product.Name,
                product.Description,
                product.CurrentUnitPrice,
                product.StockQuantity,
                product.IsActive);
        }
        public async Task<ProductModel.ResponseProduct?> GetProductById(Guid id)
        {
            _logger.LogInformation("Buscando producto con ID: {ProductId}", id);
            var product = await _repository.GetById<Product>(id);

            if (product == null)
            {
                _logger.LogWarning("Producto con ID: {ProductId} no enconrtado", id);
                throw new KeyNotFoundException($"No se encontró un producto con el ID: {id}");
            }
            _logger.LogInformation("Se mostro el producto con ID: {ProductId}", id);
            return new ProductModel.ResponseProduct(
                product.Id,
                product.Sku,
                product.InternalCode,
                product.Name,
                product.Description,
                product.CurrentUnitPrice,
                product.StockQuantity,
                product.IsActive
            );
        }

        public async Task<ProductModel.ResponsePagination> GetProducts(ProductModel.FilterProduct filter, bool includeInactive = false)
        {
            if (filter.PageNumber is <= 0 || filter.PageSize is <= 0)
                throw new InvalidDataException("La página y el tamaño deben ser mayores a cero.");
            var search = filter.Search?.ToLower();
            _logger.LogInformation("Consulta de productos. IncludeInactive: {IncludeInactive}, StatusFilter: {Status}", includeInactive, filter.Status);

            
            bool? isActiveFilter = null;
            if (!string.IsNullOrEmpty(filter.Status) && bool.TryParse(filter.Status, out bool parsedStatus))
            {
                isActiveFilter = parsedStatus;
            }

            var filteredProducts = await _repository.GetFiltered<Product>(p =>
                
                (includeInactive || p.IsActive)

                &&

                
                (!isActiveFilter.HasValue || p.IsActive == isActiveFilter.Value)

                &&

                
                (string.IsNullOrEmpty(search)
                    || (p.Name != null && p.Name.ToLower().Contains(search))
                    || (p.InternalCode != null && p.InternalCode.ToLower().Contains(search))
                    || (p.Sku != null && p.Sku.ToLower().Contains(search))
                )
            );

            if (filteredProducts == null || !filteredProducts.Any())
            {
                return new ProductModel.ResponsePagination(new List<ProductModel.ResponseProduct>(), 0);
            }

            int page = filter.PageNumber ?? 1;
            int size = filter.PageSize ?? 10;

            var products = filteredProducts
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(p => new ProductModel.ResponseProduct(
                    p.Id, p.Sku, p.InternalCode, p.Name, p.Description,
                    p.CurrentUnitPrice, p.StockQuantity, p.IsActive
                ))
                .ToList();

            return new ProductModel.ResponsePagination(products, filteredProducts.Count());
        }
        public async Task<ProductModel.ResponseProduct> UpdateProduct(Guid id, ProductModel.Request request)
        {
            _logger.LogInformation("Intentando actualizar producto con ID: {ProductId}", id);
            var existing = await _repository.GetById<Product>(id);
            if (existing == null || !existing.IsActive)
            {
                _logger.LogWarning("Intento de actualizar un producto no encontrado o inactivo con ID: {productId}", id);
                throw new KeyNotFoundException($"No se encontró producto con Id={id}.");
            }
            if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name))
            {
                _logger.LogError("Intento actualizar un producto sin poner un nombre o un sku");
                throw new InvalidDataException("El sku y el nombre son requerido");
            }
            if (request.CurrentUnitPrice < 0)
            {
                _logger.LogError("Intento actualizar un producto poniendo un precio invalido precion ingresado: {UnitPrice}", request.CurrentUnitPrice);
                throw new InvalidDataException("El precio unitario ser mayores a cero");
            }
            if (request.StockQuantity <= 0)
            {
                _logger.LogError("Intento actualizar un producto con un stock invalido valor ingresado: {stock}", request.StockQuantity);
                throw new InvalidDataException("La cantidad de stock debe ser mayor o igual a cero");
            }
            var mismoSku = await _repository.First<Product>(
                p => p.Sku == request.Sku && p.Id != id 
            );
            if (mismoSku != null)
            {
                _logger.LogError("Se intento actualizar un producto con un SKU ya existente el SKU ingresado fue: {SKU}", request.Sku);
                throw new DuplicatedEntityException($"Ya existe otro producto con SKU='{request.Sku}'.");
            }
            
            existing.Sku = request.Sku; 
            existing.InternalCode = request.InternalCode;
            existing.Name = request.Name;
            existing.Description = request.Description;
            existing.CurrentUnitPrice = request.CurrentUnitPrice;
            existing.StockQuantity = request.StockQuantity;


            var updated = await _repository.Update(existing);
            _logger.LogInformation("Producto con ID {ProductId} actualizado exitosamente.", updated.Id);

            return new ProductModel.ResponseProduct(
                updated.Id,
                updated.Sku, 
                updated.InternalCode,
                updated.Name,
                updated.Description,
                updated.CurrentUnitPrice,
                updated.StockQuantity,
                updated.IsActive
            );
        }

        public async Task DisableProduct(Guid id)
        {
            _logger.LogInformation("Intentando deshabilitar producto con ID: {ProductId}", id);
            var existing = await _repository.GetById<Product>(id);
            if (existing == null)
            {
                _logger.LogWarning("Intento de deshabilitar un producto no encontrado con ID: {ProductId}", id);
                throw new KeyNotFoundException($"No se encontró producto con Id={id}.");
            }

            existing.IsActive = false;
            _logger.LogInformation("Producto con ID {ProductId} deshabilitado exitosamente.", id);
            await _repository.Update(existing);
        }

    }
}
