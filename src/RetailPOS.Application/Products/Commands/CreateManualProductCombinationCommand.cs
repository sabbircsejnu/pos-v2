using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Commands;

public sealed record CreateManualProductCombinationCommand(
    long ProductId,
    IReadOnlyList<long> OptionIds,
    string? Sku,
    string? Barcode,
    decimal PriceAdjustment,
    decimal CostAdjustment)
    : ICommand<ProductCombinationModel>;

public sealed class CreateManualProductCombinationCommandHandler(
    IProductManualCombinationStore store,
    IProductIdentifierResolver identifierResolver)
    : IRequestHandler<CreateManualProductCombinationCommand, ProductCombinationModel>
{
    public async Task<ProductCombinationModel> Handle(
        CreateManualProductCombinationCommand request,
        CancellationToken cancellationToken)
    {
        var data = await store.GetCreationDataAsync(
                request.ProductId,
                request.OptionIds,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");

        if (data.Options.Count != request.OptionIds.Count)
        {
            throw new InvalidOperationException("Some options not found");
        }

        if (data.CombinationAlreadyExists)
        {
            throw new InvalidOperationException("This combination already exists");
        }

        if (string.IsNullOrWhiteSpace(data.ProductCode))
        {
            throw new InvalidOperationException("Main Product Code is required before creating variants");
        }

        var combinationName = string.Join(" - ", data.Options.Select(option => option.OptionName));
        var priceAdjustment = data.Options.Sum(option => option.PriceAdjustment) + request.PriceAdjustment;
        var (sizeCode, attributeCode) = identifierResolver.ExtractCodesFromOptions(
            data.Options.Select(option => (option.VariationName, option.OptionName)));
        var sku = await identifierResolver.ResolveVariantSkuAsync(
            request.Sku,
            data.ProductCode,
            sizeCode,
            attributeCode,
            cancellationToken: cancellationToken);
        var barcode = await identifierResolver.ResolveVariantBarcodeAsync(
            request.Barcode,
            data.CategoryId,
            cancellationToken: cancellationToken);

        return await store.CreateAsync(
            data,
            request.OptionIds,
            sku,
            barcode,
            priceAdjustment,
            request.CostAdjustment,
            cancellationToken);
    }
}
