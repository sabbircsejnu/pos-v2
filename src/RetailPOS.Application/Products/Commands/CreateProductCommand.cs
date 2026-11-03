using MediatR;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Products.Commands;

public sealed record ProductVariantCreation(
    string Name,
    string? Sku,
    string? Barcode,
    string? Attributes,
    decimal PriceAdjustment);

public sealed record CreateProductCommand(
    string Name,
    string? Description,
    string? ProductCode,
    string? Sku,
    string? Barcode,
    long CategoryId,
    decimal BasePrice,
    decimal? CostPrice,
    decimal? TaxRate,
    string? TaxClassification,
    string? Status,
    bool HasVariants,
    IReadOnlyList<ProductVariantCreation> Variants) : IRequest<ProductReadModel>;

public sealed class CreateProductCommandHandler(
    IProductCreateStore store,
    IProductReadStore readStore)
    : IRequestHandler<CreateProductCommand, ProductReadModel>
{
    public async Task<ProductReadModel> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        TaxClassifications.ValidateRate(request.TaxRate);
        if (await store.NameExistsAsync(request.Name, cancellationToken))
        {
            throw new InvalidOperationException($"Product with name '{request.Name}' already exists");
        }

        var sku = string.IsNullOrWhiteSpace(request.Sku)
            ? await GenerateSkuAsync(request.Name, cancellationToken)
            : request.Sku;

        if (await store.SkuExistsAsync(sku, cancellationToken))
        {
            throw new InvalidOperationException($"SKU '{sku}' already exists");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode)
            && await store.BarcodeExistsAsync(request.Barcode, cancellationToken))
        {
            throw new InvalidOperationException($"Barcode '{request.Barcode}' already exists");
        }

        if (!string.IsNullOrWhiteSpace(request.ProductCode)
            && await store.ProductCodeExistsAsync(request.ProductCode, cancellationToken))
        {
            throw new InvalidOperationException($"Product code '{request.ProductCode}' already exists");
        }

        var normalizedProductCode = string.IsNullOrWhiteSpace(request.ProductCode)
            ? null
            : request.ProductCode.Trim().ToUpper();
        if (request.HasVariants && request.Variants.Count != 0 && normalizedProductCode is null)
        {
            throw new InvalidOperationException("Main Product Code is required when creating product variants");
        }

        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            ProductCode = normalizedProductCode,
            Sku = sku,
            Barcode = request.Barcode,
            CategoryId = request.CategoryId,
            BasePrice = request.BasePrice,
            CostPrice = request.CostPrice ?? 0,
            TaxRate = request.TaxRate,
            TaxClassification = TaxClassifications.NormalizeConfigured(request.TaxClassification),
            HasVariants = request.HasVariants,
            Status = ParseStatus(request.Status)
        };

        var variants = request.HasVariants ? request.Variants : [];
        var productId = await store.CreateAsync(product, variants, cancellationToken);
        return await readStore.FindByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException($"Product with ID {productId} was not found after creation");
    }

    private async Task<string> GenerateSkuAsync(string productName, CancellationToken cancellationToken)
    {
        var prefix = new string(productName
            .ToUpper()
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .ToArray());
        if (string.IsNullOrEmpty(prefix))
        {
            prefix = "PRD";
        }

        prefix = prefix.PadRight(3, 'X');
        var originalSku = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sku = originalSku;
        var counter = 1;
        while (await store.SkuExistsAsync(sku, cancellationToken))
        {
            sku = $"{originalSku}-{counter}";
            counter++;
        }

        return sku;
    }

    private static ProductStatus ParseStatus(string? status) =>
        status?.ToLower() switch
        {
            "inactive" => ProductStatus.Inactive,
            "draft" => ProductStatus.Draft,
            _ => ProductStatus.Active
        };
}
