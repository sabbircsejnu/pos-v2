using MediatR;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Products.Commands;

public sealed record UpdateProductCommand(
    long ProductId,
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
    string? Status) : IRequest<ProductReadModel>;

public sealed class UpdateProductCommandHandler(
    IProductUpdateStore updateStore,
    IProductReadStore readStore)
    : IRequestHandler<UpdateProductCommand, ProductReadModel>
{
    public async Task<ProductReadModel> Handle(
        UpdateProductCommand request,
        CancellationToken cancellationToken)
    {
        TaxClassifications.ValidateRate(request.TaxRate);
        var current = await updateStore.FindForUpdateAsync(request.ProductId, cancellationToken);
        if (current is null)
        {
            throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");
        }

        if (await updateStore.NameExistsAsync(request.Name, request.ProductId, cancellationToken))
        {
            throw new InvalidOperationException($"Product with name '{request.Name}' already exists");
        }

        if (!string.IsNullOrWhiteSpace(request.Sku)
            && request.Sku != current.Sku
            && await updateStore.SkuExistsAsync(request.Sku, request.ProductId, cancellationToken))
        {
            throw new InvalidOperationException($"SKU '{request.Sku}' already exists");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode)
            && request.Barcode != current.Barcode
            && await updateStore.BarcodeExistsAsync(request.Barcode, request.ProductId, cancellationToken))
        {
            throw new InvalidOperationException($"Barcode '{request.Barcode}' already exists");
        }

        if (!string.IsNullOrWhiteSpace(request.ProductCode)
            && request.ProductCode != current.ProductCode
            && await updateStore.ProductCodeExistsAsync(
                request.ProductCode,
                request.ProductId,
                cancellationToken))
        {
            throw new InvalidOperationException($"Product code '{request.ProductCode}' already exists");
        }

        var values = new ProductUpdateValues(
            request.Name,
            request.Description,
            string.IsNullOrWhiteSpace(request.ProductCode)
                ? null
                : request.ProductCode.Trim().ToUpper(),
            request.Sku ?? current.Sku,
            request.Barcode,
            request.CategoryId,
            request.BasePrice,
            request.CostPrice ?? 0m,
            request.TaxRate,
            TaxClassifications.NormalizeConfigured(request.TaxClassification),
            ParseStatus(request.Status));

        await updateStore.UpdateAsync(request.ProductId, values, cancellationToken);
        return await readStore.FindByIdAsync(request.ProductId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Product with ID {request.ProductId} was not found after update");
    }

    private static ProductStatus ParseStatus(string? status) =>
        status?.ToLower() switch
        {
            "inactive" => ProductStatus.Inactive,
            "draft" => ProductStatus.Draft,
            _ => ProductStatus.Active
        };
}
