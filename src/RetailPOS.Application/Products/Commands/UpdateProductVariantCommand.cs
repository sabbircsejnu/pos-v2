using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record UpdateProductVariantCommand(
    long VariantId,
    string? Sku,
    string? Barcode,
    decimal PriceAdjustment,
    decimal CostAdjustment)
    : ICommand;

public sealed class UpdateProductVariantCommandHandler(
    IProductVariantMaintenanceStore store,
    IProductIdentifierResolver identifierResolver)
    : IRequestHandler<UpdateProductVariantCommand>
{
    public async Task Handle(UpdateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var data = await store.GetUpdateDataAsync(request.VariantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Variant with ID {request.VariantId} not found");

        if (string.IsNullOrWhiteSpace(data.ProductCode))
        {
            throw new InvalidOperationException("Main Product Code is required before updating variants");
        }

        var (sizeCode, attributeCode) = identifierResolver.ExtractCodesFromOptions(data.Options);
        var sku = await identifierResolver.ResolveVariantSkuAsync(
            request.Sku,
            data.ProductCode,
            sizeCode,
            attributeCode,
            excludeVariantId: request.VariantId,
            cancellationToken);
        var barcode = await identifierResolver.ResolveVariantBarcodeAsync(
            request.Barcode,
            data.CategoryId,
            excludeVariantId: request.VariantId,
            cancellationToken);

        await store.SaveUpdateAsync(
            request.VariantId,
            sku,
            barcode,
            request.PriceAdjustment,
            request.CostAdjustment,
            cancellationToken);
    }
}
