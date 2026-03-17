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
    private readonly ILogger<ProductVariationService> _logger;

    public ProductVariationService(
        RetailPOSDbContext context,
        IProductRepository productRepository,
        IVariationRepository variationRepository,
        IVariationOptionRepository optionRepository,
        ILogger<ProductVariationService> logger)
    {
        _context = context;
        _productRepository = productRepository;
        _variationRepository = variationRepository;
        _optionRepository = optionRepository;
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
            }).ToList()
        }).ToList();
    }

    public async Task AssignVariationsToProductAsync(long productId, List<long> variationIds)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Remove existing assignments not in the new list
            var existingIds = product.ProductVariations.Select(pv => pv.VariationId).ToList();
            var toRemove = product.ProductVariations
                .Where(pv => !variationIds.Contains(pv.VariationId))
                .ToList();

            _context.RemoveRange(toRemove);

            // Add new assignments
            var toAdd = variationIds
                .Where(id => !existingIds.Contains(id))
                .Select(id => new ProductVariation
                {
                    ProductId = productId,
                    VariationId = id,
                    IsRequired = false,
                    CreatedAt = DateTime.UtcNow
                });

            await _context.AddRangeAsync(toAdd);

            // Update product to enable variants
            product.HasVariants = variationIds.Any();
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Assigned {Count} variations to product {ProductId}", variationIds.Count, productId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<CombinationDto>> GenerateAllCombinationsAsync(long productId)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
                .ThenInclude(pv => pv.Variation)
                    .ThenInclude(v => v.Options.Where(o => o.IsActive))
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        if (!product.ProductVariations.Any())
            throw new InvalidOperationException("No variations assigned to this product");

        var variationOptions = product.ProductVariations
            .Select(pv => pv.Variation.Options.ToList())
            .ToList();

        return await GenerateCombinations(product, variationOptions);
    }

    public async Task<List<CombinationDto>> GenerateSelectedCombinationsAsync(long productId, List<long> variationIds)
    {
        var product = await _context.Products
            .Include(p => p.ProductVariations)
                .ThenInclude(pv => pv.Variation)
                    .ThenInclude(v => v.Options.Where(o => o.IsActive))
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {productId} not found");

        var variationOptions = product.ProductVariations
            .Where(pv => variationIds.Contains(pv.VariationId))
            .Select(pv => pv.Variation.Options.ToList())
            .ToList();

        if (!variationOptions.Any())
            throw new InvalidOperationException("No valid variations selected");

        return await GenerateCombinations(product, variationOptions);
    }

    private async Task<List<CombinationDto>> GenerateCombinations(Product product, List<List<VariationOption>> variationOptions)
    {
        var combinations = new List<List<VariationOption>>();
        GenerateCombinationsRecursive(variationOptions, 0, new List<VariationOption>(), combinations);

        var result = new List<CombinationDto>();
        int skippedCount = 0;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var combination in combinations)
            {
                // Check if combination already exists
                var optionIds = combination.Select(o => o.Id).OrderBy(id => id).ToList();
                var existingVariant = await _context.ProductVariants
                    .Include(pv => pv.ProductVariantOptions)
                    .Where(pv => pv.ProductId == product.Id && pv.ProductVariantOptions.Count == optionIds.Count)
                    .ToListAsync();

                var exists = existingVariant.Any(ev =>
                    ev.ProductVariantOptions.Select(pvo => pvo.OptionId).OrderBy(id => id).SequenceEqual(optionIds));

                if (exists)
                {
                    skippedCount++;
                    continue;
                }

                // Create new variant
                var combinationName = string.Join(" - ", combination.Select(o => o.Name));
                var priceAdjustment = combination.Sum(o => o.PriceAdjustment);

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Name = combinationName,
                    Sku = $"{product.Sku}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}",
                    PriceAdjustment = priceAdjustment,
                    CostAdjustment = 0,
                    Attributes = "{}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ProductVariants.Add(variant);
                await _context.SaveChangesAsync(); // Save to get variant ID

                // Add variant-option mappings
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

            _logger.LogInformation("Generated {CreatedCount} new combinations for product {ProductId}, skipped {SkippedCount} duplicates", 
                result.Count, product.Id, skippedCount);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private void GenerateCombinationsRecursive(
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

        // Check if combination already exists
        var optionIds = request.OptionIds.OrderBy(id => id).ToList();
        var existingVariant = await _context.ProductVariants
            .Include(pv => pv.ProductVariantOptions)
            .Where(pv => pv.ProductId == productId && pv.ProductVariantOptions.Count == optionIds.Count)
            .ToListAsync();

        var exists = existingVariant.Any(ev =>
            ev.ProductVariantOptions.Select(pvo => pvo.OptionId).OrderBy(id => id).SequenceEqual(optionIds));

        if (exists)
            throw new InvalidOperationException("This combination already exists");

        var combinationName = string.Join(" - ", options.Select(o => o.Name));
        var priceAdjustment = options.Sum(o => o.PriceAdjustment) + request.PriceAdjustment;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var variant = new ProductVariant
            {
                ProductId = productId,
                Name = combinationName,
                Sku = request.Sku ?? $"{product.Sku}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}",
                Barcode = request.Barcode,
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
        var variant = await _context.ProductVariants.FindAsync(variantId);
        if (variant == null)
            throw new KeyNotFoundException($"Variant with ID {variantId} not found");

        variant.Sku = request.Sku;
        variant.Barcode = request.Barcode;
        variant.PriceAdjustment = request.PriceAdjustment;
        variant.CostAdjustment = request.CostAdjustment;
        variant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteCombinationAsync(long variantId)
    {
        var variant = await _context.ProductVariants.FindAsync(variantId);
        if (variant == null)
            throw new KeyNotFoundException($"Variant with ID {variantId} not found");

        _context.ProductVariants.Remove(variant);
        await _context.SaveChangesAsync();
    }
}
