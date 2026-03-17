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
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        IProductVariantRepository variantRepository,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
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
            searchDto.IsActive,
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

        // Create product entity
        var product = new Product
        {
            Name = createDto.Name,
            Description = createDto.Description,
            Sku = sku,
            Barcode = createDto.Barcode,
            CategoryId = createDto.CategoryId,
            BasePrice = createDto.BasePrice,
            CostPrice = createDto.CostPrice ?? 0,
            TaxRate = createDto.TaxRate,
            HasVariants = createDto.HasVariants,
            ImageUrl = createDto.ImageUrl,
            IsActive = createDto.IsActive
        };

        // Save product first
        var createdProduct = await _productRepository.CreateAsync(product);

        // Create variants if specified
        if (createDto.HasVariants && createDto.Variants != null && createDto.Variants.Any())
        {
            foreach (var variantDto in createDto.Variants)
            {
                // Generate variant SKU if not provided
                var variantSku = string.IsNullOrWhiteSpace(variantDto.Sku)
                    ? $"{sku}-{variantDto.Name.Replace(" ", "").ToUpper().Substring(0, Math.Min(3, variantDto.Name.Length))}"
                    : variantDto.Sku;

                // Validate variant SKU uniqueness
                if (await _variantRepository.SkuExistsAsync(variantSku))
                {
                    throw new InvalidOperationException($"Variant SKU '{variantSku}' already exists");
                }

                // Validate variant Barcode uniqueness (if provided)
                if (!string.IsNullOrWhiteSpace(variantDto.Barcode) &&
                    await _variantRepository.BarcodeExistsAsync(variantDto.Barcode))
                {
                    throw new InvalidOperationException($"Variant Barcode '{variantDto.Barcode}' already exists");
                }

                var variant = new ProductVariant
                {
                    ProductId = createdProduct.Id,
                    Name = variantDto.Name,
                    Sku = variantSku,
                    Barcode = variantDto.Barcode,
                    Attributes = variantDto.Attributes,
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

        // Update product properties
        product.Name = updateDto.Name;
        product.Description = updateDto.Description;
        product.Sku = updateDto.Sku ?? product.Sku;
        product.Barcode = updateDto.Barcode;
        product.CategoryId = updateDto.CategoryId;
        product.BasePrice = updateDto.BasePrice;
        product.CostPrice = updateDto.CostPrice ?? 0;
        product.TaxRate = updateDto.TaxRate;
        product.ImageUrl = updateDto.ImageUrl;
        product.IsActive = updateDto.IsActive;

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
            Sku = product.Sku,
            Barcode = product.Barcode,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name ?? string.Empty,
            BasePrice = product.BasePrice,
            CostPrice = product.CostPrice,
            TaxRate = product.TaxRate,
            HasVariants = product.HasVariants,
            ImageUrl = product.ImageUrl,
            IsActive = product.IsActive,
            VariantCount = variants.Count,
            TotalStock = totalStock,
            Variants = variants,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }

    public async Task<IEnumerable<ProductVariantSearchDto>> SearchVariantsAsync(string query, int pageNumber, int pageSize)
    {
        var variants = await _variantRepository.SearchAsync(query, pageNumber, pageSize);
        
        var result = new List<ProductVariantSearchDto>();
        
        foreach (var variant in variants)
        {
            // Get the product for each variant
            var product = await _productRepository.GetByIdAsync(variant.ProductId);
            if (product == null) continue;
            
            // For single-variant products with "Default" name, use product name only
            var variantDisplayName = variant.Name;
            if (string.IsNullOrWhiteSpace(variantDisplayName) || 
                variantDisplayName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                variantDisplayName = "Standard";
            }
            
            result.Add(new ProductVariantSearchDto
            {
                Id = variant.Id,
                ProductId = variant.ProductId,
                ProductName = product.Name,
                Name = variantDisplayName,
                Sku = variant.Sku,
                Barcode = variant.Barcode,
                Attributes = variant.Attributes,
                FinalPrice = product.BasePrice + variant.PriceAdjustment,
                CostPrice = product.CostPrice,
                StockQuantity = 0 // TODO: Implement when Inventory module is ready
            });
        }
        
        return result;
    }
}
