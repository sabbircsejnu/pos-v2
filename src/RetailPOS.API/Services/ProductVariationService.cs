using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Product;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class ProductVariationService : IProductVariationService
{
    private readonly RetailPOSDbContext _context;
    private readonly IProductRepository _productRepository;
    private readonly IVariationRepository _variationRepository;
    private readonly IVariationOptionRepository _optionRepository;
    private readonly IProductIdentifierService _identifierService;
    private readonly ILogger<ProductVariationService> _logger;

    public ProductVariationService(
        RetailPOSDbContext context,
        IProductRepository productRepository,
        IVariationRepository variationRepository,
        IVariationOptionRepository optionRepository,
        IProductIdentifierService identifierService,
        ILogger<ProductVariationService> logger)
    {
        _context = context;
        _productRepository = productRepository;
        _variationRepository = variationRepository;
        _optionRepository = optionRepository;
        _identifierService = identifierService;
        _logger = logger;
    }

    public async Task<List<ProductVariationDto>> GetProductVariationsAsync(long productId)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
                .ThenInclude(pv => pv.Variation)
                    .ThenInclude(v => v.Options.OrderBy(o => o.DisplayOrder))
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        // Load which specific options are selected for this product
        var selectedOptions = await _context.ProductVariationSelectedOptions
            .Where(s => s.ProductId == productId)
            .ToListAsync();

        var selectedByVariation = selectedOptions
            .GroupBy(s => s.VariationId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.OptionId).ToList());

        return product.ProductVariations.Select(pv => new ProductVariationDto
        {
            VariationId = pv.VariationId,
            VariationName = pv.Variation.Name,
            IsRequired = pv.IsRequired,
            Options = pv.Variation.Options.Select(o => new VariationOptionInfo
            {
                Id = o.Id,
                Name = o.Name,
                PriceAdjustment = o.PriceAdjustment
            }).ToList(),
            // If no specific options saved yet, default to all options being selected
            SelectedOptionIds = selectedByVariation.TryGetValue(pv.VariationId, out var selIds) && selIds.Count > 0
                ? selIds
                : pv.Variation.Options.Select(o => o.Id).ToList()
        }).ToList();
    }

    public async Task AssignVariationsToProductAsync(
        long productId,
        List<long> variationIds,
        Dictionary<long, List<long>> selectedOptionsByVariation)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // ── Variation-type assignment ─────────────────────────────────────────────
            var existingVariationIds = product.ProductVariations.Select(pv => pv.VariationId).ToList();
            var toRemove = product.ProductVariations
                .Where(pv => !variationIds.Contains(pv.VariationId))
                .ToList();
            _context.RemoveRange(toRemove);

            var toAdd = variationIds
                .Where(id => !existingVariationIds.Contains(id))
                .Select(id => new ProductVariation
                {
                    ProductId = productId,
                    VariationId = id,
                    IsRequired = false,
                    CreatedAt = DateTime.UtcNow
                });
            await _context.AddRangeAsync(toAdd);

            product.HasVariants = variationIds.Any();
            product.UpdatedAt = DateTime.UtcNow;

            // ── Selected options per variation ────────────────────────────────────────
            // Remove all existing selected-option rows for this product
            var existingSelected = await _context.ProductVariationSelectedOptions
                .Where(s => s.ProductId == productId)
                .ToListAsync();
            _context.RemoveRange(existingSelected);

            // Add newly selected options for each assigned variation
            foreach (var variationId in variationIds)
            {
                List<long> optionIds;

                if (selectedOptionsByVariation.TryGetValue(variationId, out var provided) && provided.Count > 0)
                {
                    optionIds = provided;
                }
                else
                {
                    // Default: select all active options for this variation
                    optionIds = await _context.VariationOptions
                        .Where(o => o.VariationId == variationId && o.IsActive)
                        .Select(o => o.Id)
                        .ToListAsync();
                }

                var newSelected = optionIds.Select(optId => new ProductVariationSelectedOption
                {
                    ProductId = productId,
                    VariationId = variationId,
                    OptionId = optId,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.AddRangeAsync(newSelected);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Assigned {Count} variations to product {ProductId} with {OptionCount} total selected options",
                variationIds.Count, productId,
                selectedOptionsByVariation.Values.Sum(v => v.Count));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Generates combinations using only the product-selected options for each
    /// assigned variation type (not all global options).
    /// </summary>
    public async Task<List<CombinationDto>> GenerateAllCombinationsAsync(long productId)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
                .ThenInclude(pv => pv.Variation)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        if (!product.ProductVariations.Any())
            throw new InvalidOperationException("No variations assigned to this product");

        var variationOptions = await GetSelectedOptionsForProduct(productId, null);

        if (variationOptions.Any(opts => opts.Count == 0))
            throw new InvalidOperationException(
                "One or more selected variations have no options selected. " +
                "Please select at least one option per variation before generating combinations.");

        return await GenerateCombinations(product, variationOptions);
    }

    /// <summary>
    /// Generates combinations for the specified variation types using only the
    /// product-selected options.
    /// </summary>
    public async Task<List<CombinationDto>> GenerateSelectedCombinationsAsync(long productId, List<long> variationIds)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        // Validate variationIds against what's actually assigned to this product
        var assignedVariationIds = product.ProductVariations.Select(pv => pv.VariationId).ToHashSet();

        if (!assignedVariationIds.Any())
            throw new InvalidOperationException(
                $"Product {productId} has no variations assigned. Please assign variations to this product first.");

        if (variationIds.Any())
        {
            var invalidIds = variationIds.Where(id => !assignedVariationIds.Contains(id)).ToList();
            if (invalidIds.Any())
                throw new InvalidOperationException(
                    $"Variation ID(s) {string.Join(", ", invalidIds)} are not assigned to product {productId}. " +
                    $"Assigned variation IDs are: {string.Join(", ", assignedVariationIds.OrderBy(id => id))}.");
        }

        var variationOptions = await GetSelectedOptionsForProduct(productId, variationIds);

        if (!variationOptions.Any())
            throw new InvalidOperationException("No valid variations selected");

        if (variationOptions.Any(opts => opts.Count == 0))
            throw new InvalidOperationException(
                "One or more selected variations have no options selected for this product.");

        return await GenerateCombinations(product, variationOptions);
    }

    /// <summary>
    /// Loads the product-selected options (from product_variation_selected_options) grouped
    /// by variation. Each inner list contains the VariationOption entities to use for Cartesian
    /// product generation.
    /// </summary>
    private async Task<List<List<VariationOption>>> GetSelectedOptionsForProduct(
        long productId,
        List<long>? filterVariationIds)
    {
        var query = _context.ProductVariationSelectedOptions
            .Include(s => s.Option)
                .ThenInclude(o => o.Variation)
            .Where(s => s.ProductId == productId);

        if (filterVariationIds != null && filterVariationIds.Count > 0)
            query = query.Where(s => filterVariationIds.Contains(s.VariationId));

        var selected = await query.ToListAsync();

        if (!selected.Any())
        {
            // Fallback for products that haven't gone through the new assignment flow:
            // load all assigned variation types and use ALL their active options.
            _logger.LogWarning(
                "Product {ProductId} has no selected options recorded; falling back to all active options.",
                productId);

            var fallbackQuery = _context.ProductVariations
                .Include(pv => pv.Variation)
                    .ThenInclude(v => v.Options.Where(o => o.IsActive))
                .Where(pv => pv.ProductId == productId);

            if (filterVariationIds != null && filterVariationIds.Count > 0)
                fallbackQuery = fallbackQuery.Where(pv => filterVariationIds.Contains(pv.VariationId));

            var assigned = await fallbackQuery.ToListAsync();
            return assigned.Select(pv => pv.Variation.Options.ToList()).ToList();
        }

        // Group by variation, preserving the option display order
        return selected
            .GroupBy(s => s.VariationId)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(s => s.Option)
                          .OrderBy(o => o.DisplayOrder)
                          .ToList())
            .ToList();
    }

    private async Task<List<CombinationDto>> GenerateCombinations(Product product, List<List<VariationOption>> variationOptions)
    {
        if (string.IsNullOrWhiteSpace(product.ProductCode))
            throw new InvalidOperationException("Main Product Code is required before generating variants");

        var combinations = new List<List<VariationOption>>();
        GenerateCombinationsRecursive(variationOptions, 0, new List<VariationOption>(), combinations);

        var result = new List<CombinationDto>();
        int skippedCount = 0;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var combination in combinations)
            {
                var optionIds = combination.Select(o => o.Id).OrderBy(id => id).ToList();
                var existingVariants = await _context.ProductVariants
                    .Include(pv => pv.ProductVariantOptions)
                    .Where(pv => pv.ProductId == product.Id && pv.ProductVariantOptions.Count == optionIds.Count)
                    .ToListAsync();

                var exists = existingVariants.Any(ev =>
                    ev.ProductVariantOptions.Select(pvo => pvo.OptionId).OrderBy(id => id).SequenceEqual(optionIds));

                if (exists)
                {
                    skippedCount++;
                    continue;
                }

                var combinationName = string.Join(" - ", combination.Select(o => o.Name));
                var priceAdjustment = combination.Sum(o => o.PriceAdjustment);
                var (sizeCode, attrCode) = _identifierService.ExtractCodesFromOptions(
                    combination.Select(o => (o.Variation?.Name ?? string.Empty, o.Name)));

                var generatedSku = await _identifierService.ResolveVariantSkuAsync(
                    requestedSku: null,
                    mainProductCode: product.ProductCode,
                    sizeCode: sizeCode,
                    attributeCode: attrCode);

                var generatedBarcode = await _identifierService.ResolveVariantBarcodeAsync(
                    requestedBarcode: null,
                    categoryId: product.CategoryId);

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Name = combinationName,
                    Sku = generatedSku,
                    Barcode = generatedBarcode,
                    PriceAdjustment = priceAdjustment,
                    CostAdjustment = 0,
                    Attributes = "{}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ProductVariants.Add(variant);
                await _context.SaveChangesAsync();

                foreach (var option in combination)
                {
                    _context.Add(new ProductVariantOption
                    {
                        VariantId = variant.Id,
                        OptionId = option.Id
                    });
                }

                result.Add(new CombinationDto
                {
                    Id = variant.Id,
                    Sku = variant.Sku,
                    Barcode = variant.Barcode,
                    PriceAdjustment = variant.PriceAdjustment,
                    CostAdjustment = variant.CostAdjustment,
                    OptionIds = optionIds,
                    Options = combination.Select(o => new CombinationOptionInfo
                    {
                        Id = o.Id,
                        VariationName = o.Variation?.Name ?? "",
                        OptionName = o.Name,
                        PriceAdjustment = o.PriceAdjustment
                    }).ToList(),
                    CombinationName = combinationName,
                    FinalPrice = product.BasePrice + priceAdjustment
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Generated {CreatedCount} new combinations for product {ProductId}, skipped {SkippedCount} duplicates",
                result.Count, product.Id, skippedCount);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void GenerateCombinationsRecursive(
        List<List<VariationOption>> variationOptions,
        int currentIndex,
        List<VariationOption> currentCombination,
        List<List<VariationOption>> result)
    {
        if (currentIndex == variationOptions.Count)
        {
            result.Add(new List<VariationOption>(currentCombination));
            return;
        }

        foreach (var option in variationOptions[currentIndex])
        {
            currentCombination.Add(option);
            GenerateCombinationsRecursive(variationOptions, currentIndex + 1, currentCombination, result);
            currentCombination.RemoveAt(currentCombination.Count - 1);
        }
    }

    public async Task<CombinationDto> CreateManualCombinationAsync(long productId, CreateCombinationRequest request)
    {
        var product = await _productRepository.GetByIdAsync(productId);
        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        var options = await _optionRepository.GetByIdsAsync(request.OptionIds);
        if (options.Count != request.OptionIds.Count)
            throw new InvalidOperationException("Some options not found");

        var optionIds = request.OptionIds.OrderBy(id => id).ToList();
        var existingVariants = await _context.ProductVariants
            .Include(pv => pv.ProductVariantOptions)
            .Where(pv => pv.ProductId == productId && pv.ProductVariantOptions.Count == optionIds.Count)
            .ToListAsync();

        var exists = existingVariants.Any(ev =>
            ev.ProductVariantOptions.Select(pvo => pvo.OptionId).OrderBy(id => id).SequenceEqual(optionIds));

        if (exists)
            throw new InvalidOperationException("This combination already exists");

        var combinationName = string.Join(" - ", options.Select(o => o.Name));
        var priceAdjustment = options.Sum(o => o.PriceAdjustment) + request.PriceAdjustment;

        if (string.IsNullOrWhiteSpace(product.ProductCode))
            throw new InvalidOperationException("Main Product Code is required before creating variants");

        var (sizeCode, attrCode) = _identifierService.ExtractCodesFromOptions(
            options.Select(o => (o.Variation?.Name ?? string.Empty, o.Name)));

        var resolvedSku = await _identifierService.ResolveVariantSkuAsync(
            request.Sku,
            product.ProductCode,
            sizeCode,
            attrCode);

        var resolvedBarcode = await _identifierService.ResolveVariantBarcodeAsync(
            request.Barcode,
            product.CategoryId);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var variant = new ProductVariant
            {
                ProductId = productId,
                Name = combinationName,
                Sku = resolvedSku,
                Barcode = resolvedBarcode,
                PriceAdjustment = priceAdjustment,
                CostAdjustment = request.CostAdjustment,
                Attributes = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.ProductVariants.Add(variant);
            await _context.SaveChangesAsync();

            foreach (var optionId in request.OptionIds)
            {
                _context.Add(new ProductVariantOption
                {
                    VariantId = variant.Id,
                    OptionId = optionId
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new CombinationDto
            {
                Id = variant.Id,
                Sku = variant.Sku,
                Barcode = variant.Barcode,
                PriceAdjustment = variant.PriceAdjustment,
                CostAdjustment = variant.CostAdjustment,
                OptionIds = request.OptionIds,
                Options = options.Select(o => new CombinationOptionInfo
                {
                    Id = o.Id,
                    VariationName = o.Variation?.Name ?? "",
                    OptionName = o.Name,
                    PriceAdjustment = o.PriceAdjustment
                }).ToList(),
                CombinationName = combinationName,
                FinalPrice = product.BasePrice + priceAdjustment
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<CombinationDto>> GetProductCombinationsAsync(long productId)
    {
        var product = await _productRepository.GetByIdAsync(productId);
        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        var variants = await _context.ProductVariants
            .Include(pv => pv.ProductVariantOptions)
                .ThenInclude(pvo => pvo.Option)
                    .ThenInclude(o => o.Variation)
            .Where(pv => pv.ProductId == productId)
            .ToListAsync();

        return variants.Select(v => new CombinationDto
        {
            Id = v.Id,
            Sku = v.Sku,
            Barcode = v.Barcode,
            PriceAdjustment = v.PriceAdjustment,
            CostAdjustment = v.CostAdjustment,
            OptionIds = v.ProductVariantOptions.Select(pvo => pvo.OptionId).ToList(),
            Options = v.ProductVariantOptions.Select(pvo => new CombinationOptionInfo
            {
                Id = pvo.Option.Id,
                VariationName = pvo.Option.Variation?.Name ?? "",
                OptionName = pvo.Option.Name,
                PriceAdjustment = pvo.Option.PriceAdjustment
            }).ToList(),
            CombinationName = v.Name,
            FinalPrice = product.BasePrice + v.PriceAdjustment
        }).ToList();
    }

    public async Task UpdateCombinationAsync(long variantId, UpdateCombinationRequest request)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.Product)
            .Include(v => v.ProductVariantOptions)
                .ThenInclude(pvo => pvo.Option)
                    .ThenInclude(o => o.Variation)
            .FirstOrDefaultAsync(v => v.Id == variantId);

        if (variant == null)
            throw new KeyNotFoundException($"Variant with ID {variantId} not found");

        if (string.IsNullOrWhiteSpace(variant.Product.ProductCode))
            throw new InvalidOperationException("Main Product Code is required before updating variants");

        var (sizeCode, attrCode) = _identifierService.ExtractCodesFromOptions(
            variant.ProductVariantOptions.Select(pvo => (pvo.Option.Variation?.Name ?? string.Empty, pvo.Option.Name)));

        var resolvedSku = await _identifierService.ResolveVariantSkuAsync(
            request.Sku,
            variant.Product.ProductCode,
            sizeCode,
            attrCode,
            excludeVariantId: variantId);

        var resolvedBarcode = await _identifierService.ResolveVariantBarcodeAsync(
            request.Barcode,
            variant.Product.CategoryId,
            excludeVariantId: variantId);

        variant.Sku = resolvedSku;
        variant.Barcode = resolvedBarcode;
        variant.PriceAdjustment = request.PriceAdjustment;
        variant.CostAdjustment = request.CostAdjustment;
        variant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteCombinationAsync(long variantId)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.Inventories)
            .Include(v => v.SaleItems)
            .Include(v => v.PurchaseOrderItems)
            .Include(v => v.StockTransferItems)
            .Include(v => v.StockAdjustmentLines)
            .FirstOrDefaultAsync(v => v.Id == variantId);

        if (variant == null)
            throw new KeyNotFoundException($"Variant with ID {variantId} not found");

        if (variant.SaleItems.Any())
            throw new InvalidOperationException("Cannot delete a variant that has associated sales records.");
        if (variant.PurchaseOrderItems.Any())
            throw new InvalidOperationException("Cannot delete a variant that has associated purchase order items.");
        if (variant.StockTransferItems.Any())
            throw new InvalidOperationException("Cannot delete a variant that has associated stock transfer items.");
        if (variant.StockAdjustmentLines.Any())
            throw new InvalidOperationException("Cannot delete a variant that has associated stock adjustments.");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Inventories.RemoveRange(variant.Inventories);
            _context.ProductVariants.Remove(variant);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

