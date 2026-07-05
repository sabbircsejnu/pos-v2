using RetailPOS.API.DTOs.Product;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;
using System.Text;
using System.Text.Json;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Product operations
/// </summary>
public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IProductVariantRepository _variantRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IProductIdentifierService _identifierService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        IProductVariantRepository variantRepository,
        IInventoryRepository inventoryRepository,
        IProductIdentifierService identifierService,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _inventoryRepository = inventoryRepository;
        _identifierService = identifierService;
        _logger = logger;
    }

    public async Task<ProductDto?> GetByIdAsync(long id)
    {
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
            return null;

        return await MapToDto(product);
    }

    public async Task<ProductDto?> GetBySkuAsync(string sku)
    {
        var product = await _productRepository.GetBySkuAsync(sku);
        if (product == null)
            return null;

        return await MapToDto(product);
    }

    public async Task<ProductDto?> GetByBarcodeAsync(string barcode)
    {
        var product = await _productRepository.GetByBarcodeAsync(barcode);
        if (product == null)
            return null;

        return await MapToDto(product);
    }

    public async Task<IEnumerable<ProductDto>> GetAllAsync()
    {
        var products = await _productRepository.GetAllAsync();
        var productDtos = new List<ProductDto>();

        foreach (var product in products)
        {
            productDtos.Add(await MapToDto(product));
        }

        return productDtos;
    }

    public async Task<ProductListDto> SearchAsync(ProductSearchDto searchDto)
    {
        var (products, totalCount) = await _productRepository.SearchAsync(
            searchDto.SearchQuery,
            searchDto.CategoryId,
            searchDto.Status,
            searchDto.HasVariants,
            searchDto.MinPrice,
            searchDto.MaxPrice,
            searchDto.PageNumber,
            searchDto.PageSize,
            searchDto.SortBy,
            searchDto.SortOrder);

        var productDtos = new List<ProductDto>();
        foreach (var product in products)
        {
            productDtos.Add(await MapToDto(product));
        }

        return new ProductListDto
        {
            Products = productDtos,
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<IEnumerable<ProductDto>> GetByCategoryAsync(long categoryId)
    {
        var products = await _productRepository.GetByCategoryAsync(categoryId);
        var productDtos = new List<ProductDto>();

        foreach (var product in products)
        {
            productDtos.Add(await MapToDto(product));
        }

        return productDtos;
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto createDto)
    {
        // Validate category exists (if specified)
        // This would be done by the repository when saving

        // Check for duplicate product name
        if (await _productRepository.ExistsAsync(createDto.Name))
        {
            throw new InvalidOperationException($"Product with name '{createDto.Name}' already exists");
        }

        // Generate SKU if not provided
        var sku = string.IsNullOrWhiteSpace(createDto.Sku)
            ? await GenerateSkuAsync(createDto.Name)
            : createDto.Sku;

        // Validate SKU uniqueness
        if (await _productRepository.SkuExistsAsync(sku))
        {
            throw new InvalidOperationException($"SKU '{sku}' already exists");
        }

        // Validate Barcode uniqueness (if provided)
        if (!string.IsNullOrWhiteSpace(createDto.Barcode) &&
            await _productRepository.BarcodeExistsAsync(createDto.Barcode))
        {
            throw new InvalidOperationException($"Barcode '{createDto.Barcode}' already exists");
        }

        // Validate ProductCode uniqueness (if provided)
        if (!string.IsNullOrWhiteSpace(createDto.ProductCode) &&
            await _productRepository.ProductCodeExistsAsync(createDto.ProductCode))
        {
            throw new InvalidOperationException($"Product code '{createDto.ProductCode}' already exists");
        }

        // Create product entity
        var product = new Product
        {
            Name = createDto.Name,
            Description = createDto.Description,
            ProductCode = string.IsNullOrWhiteSpace(createDto.ProductCode) ? null : createDto.ProductCode.Trim().ToUpper(),
            Sku = sku,
            Barcode = createDto.Barcode,
            CategoryId = createDto.CategoryId,
            BasePrice = createDto.BasePrice,
            CostPrice = createDto.CostPrice ?? 0,
            TaxRate = createDto.TaxRate,
            HasVariants = createDto.HasVariants,
            Status = ParseStatus(createDto.Status)
        };

        // Save product first
        var createdProduct = await _productRepository.CreateAsync(product);

        // Create variants if specified
        if (createDto.HasVariants && createDto.Variants != null && createDto.Variants.Any())
        {
            if (string.IsNullOrWhiteSpace(createdProduct.ProductCode))
            {
                throw new InvalidOperationException("Main Product Code is required when creating product variants");
            }

            foreach (var variantDto in createDto.Variants)
            {
                var (sizeCode, attributeCode) = _identifierService.ExtractCodesFromVariantText(
                    variantDto.Name,
                    variantDto.Attributes);

                var variantSku = await _identifierService.ResolveVariantSkuAsync(
                    variantDto.Sku,
                    createdProduct.ProductCode,
                    sizeCode,
                    attributeCode);

                var variantBarcode = await _identifierService.ResolveVariantBarcodeAsync(
                    variantDto.Barcode,
                    createdProduct.CategoryId);

                var variant = new ProductVariant
                {
                    ProductId = createdProduct.Id,
                    Name = variantDto.Name,
                    Sku = variantSku,
                    Barcode = variantBarcode,
                    Attributes = variantDto.Attributes ?? "{}",
                    PriceAdjustment = variantDto.PriceAdjustment
                };

                await _variantRepository.CreateAsync(variant);
            }
        }

        // Reload product with variants
        var productWithVariants = await _productRepository.GetByIdAsync(createdProduct.Id);
        return await MapToDto(productWithVariants!);
    }

    public async Task<ProductDto> UpdateAsync(long id, UpdateProductDto updateDto)
    {
        var product = await _productRepository.GetByIdAsync(id, includeVariants: false);
        if (product == null)
        {
            throw new KeyNotFoundException($"Product with ID {id} not found");
        }

        // Check for duplicate product name (excluding current product)
        if (await _productRepository.ExistsAsync(updateDto.Name, id))
        {
            throw new InvalidOperationException($"Product with name '{updateDto.Name}' already exists");
        }

        // Validate SKU uniqueness (if changed and provided)
        if (!string.IsNullOrWhiteSpace(updateDto.Sku) &&
            updateDto.Sku != product.Sku &&
            await _productRepository.SkuExistsAsync(updateDto.Sku, id))
        {
            throw new InvalidOperationException($"SKU '{updateDto.Sku}' already exists");
        }

        // Validate Barcode uniqueness (if changed and provided)
        if (!string.IsNullOrWhiteSpace(updateDto.Barcode) &&
            updateDto.Barcode != product.Barcode &&
            await _productRepository.BarcodeExistsAsync(updateDto.Barcode, id))
        {
            throw new InvalidOperationException($"Barcode '{updateDto.Barcode}' already exists");
        }

        // Validate ProductCode uniqueness (if changed and provided)
        if (!string.IsNullOrWhiteSpace(updateDto.ProductCode) &&
            updateDto.ProductCode != product.ProductCode &&
            await _productRepository.ProductCodeExistsAsync(updateDto.ProductCode, id))
        {
            throw new InvalidOperationException($"Product code '{updateDto.ProductCode}' already exists");
        }

        // Update product properties
        product.Name = updateDto.Name;
        product.Description = updateDto.Description;
        product.ProductCode = string.IsNullOrWhiteSpace(updateDto.ProductCode) ? null : updateDto.ProductCode.Trim().ToUpper();
        product.Sku = updateDto.Sku ?? product.Sku;
        product.Barcode = updateDto.Barcode;
        product.CategoryId = updateDto.CategoryId;
        product.BasePrice = updateDto.BasePrice;
        product.CostPrice = updateDto.CostPrice ?? 0;
        product.TaxRate = updateDto.TaxRate;
        product.Status = ParseStatus(updateDto.Status);

        await _productRepository.UpdateAsync(product);

        // Reload product with variants
        var updatedProduct = await _productRepository.GetByIdAsync(id);
        return await MapToDto(updatedProduct!);
    }

    public async Task DeleteAsync(long id)
    {
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            throw new KeyNotFoundException($"Product with ID {id} not found");
        }

        // Check if product has any inventory
        var totalStock = await _productRepository.GetTotalStockAsync(id);
        if (totalStock > 0)
        {
            throw new InvalidOperationException($"Cannot delete product with existing inventory (Stock: {totalStock})");
        }

        await _productRepository.DeleteAsync(id);
    }

    public async Task<string> GenerateSkuAsync(string productName)
    {
        // Generate SKU from product name + timestamp
        var prefix = new string(productName
            .ToUpper()
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .ToArray());

        if (string.IsNullOrEmpty(prefix))
        {
            prefix = "PRD";
        }

        // Pad if less than 3 characters
        prefix = prefix.PadRight(3, 'X');

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var sku = $"{prefix}-{timestamp}";

        // Ensure uniqueness
        var counter = 1;
        var originalSku = sku;
        while (await _productRepository.SkuExistsAsync(sku))
        {
            sku = $"{originalSku}-{counter}";
            counter++;
        }

        return sku;
    }

    private async Task<ProductDto> MapToDto(Product product)
    {
        var totalStock = await _productRepository.GetTotalStockAsync(product.Id);

        var variants = product.ProductVariants?
            .Select(v => new ProductVariantDto
            {
                Id = v.Id,
                ProductId = v.ProductId,
                ProductCode = product.ProductCode,
                Name = v.Name,
                Sku = v.Sku,
                Barcode = v.Barcode,
                Attributes = v.Attributes,
                PriceAdjustment = v.PriceAdjustment,
                FinalPrice = product.BasePrice + v.PriceAdjustment,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt
            })
            .ToList() ?? new List<ProductVariantDto>();

        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            ProductCode = product.ProductCode,
            Sku = product.Sku,
            Barcode = product.Barcode,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name ?? string.Empty,
            BasePrice = product.BasePrice,
            CostPrice = product.CostPrice,
            TaxRate = product.TaxRate,
            HasVariants = product.HasVariants,
            PrimaryImageThumb = product.Images?.FirstOrDefault(i => i.IsPrimary)?.ThumbPath,
            PrimaryImageMedium = product.Images?.FirstOrDefault(i => i.IsPrimary)?.MediumPath,
            Status = product.Status.ToString().ToLower(),
            IsActive = product.IsActive,
            VariantCount = variants.Count,
            TotalStock = totalStock,
            Variants = variants,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }

    public async Task<IEnumerable<ProductVariantSearchDto>> SearchVariantsAsync(string query, int pageNumber, int pageSize, long? locationId = null, string? locationType = null)
    {
        var variants = await _variantRepository.SearchAsync(query, pageNumber, pageSize);
        
        var result = new List<ProductVariantSearchDto>();

        var normalizedLocationType = string.IsNullOrWhiteSpace(locationType)
            ? null
            : locationType.Trim().ToLowerInvariant();
        
        foreach (var variant in variants)
        {
            var product = variant.Product ?? await _productRepository.GetByIdAsync(variant.ProductId);
            if (product == null) continue;
            
            // For single-variant products with "Default" name, use product name only
            var variantDisplayName = variant.Name;
            if (string.IsNullOrWhiteSpace(variantDisplayName) || 
                variantDisplayName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                variantDisplayName = "Standard";
            }
            
            int? stockQuantity = null;
            if (locationId.HasValue && (normalizedLocationType == "outlet" || normalizedLocationType == "warehouse"))
            {
                var inventory = await _inventoryRepository.GetByVariantAndLocationAsync(variant.Id, locationId.Value, normalizedLocationType);
                stockQuantity = inventory?.Quantity ?? 0;
            }

            result.Add(new ProductVariantSearchDto
            {
                Id = variant.Id,
                ProductId = variant.ProductId,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Name = variantDisplayName,
                Sku = variant.Sku,
                Barcode = variant.Barcode,
                Attributes = variant.Attributes,
                FinalPrice = product.BasePrice + variant.PriceAdjustment,
                CostPrice = product.CostPrice,
                StockQuantity = stockQuantity,
                PrimaryImageThumb = product.Images?.FirstOrDefault(i => i.IsPrimary)?.ThumbPath,
            });
        }
        
        return result;
    }

    private static ProductStatus ParseStatus(string? status) =>
        status?.ToLower() switch
        {
            "inactive" => ProductStatus.Inactive,
            "draft"    => ProductStatus.Draft,
            _          => ProductStatus.Active   // default and "active"
        };
}
