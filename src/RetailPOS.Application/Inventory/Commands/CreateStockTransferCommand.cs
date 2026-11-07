using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record CreateStockTransferCommand(
    StockTransferCreateInput Input,
    long? CreatedBy)
    : ICommand<StockTransferReadModel>;

public sealed class CreateStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferCreationStore creationStore,
    IStockTransferReadStore readStore,
    RetailPOS.Application.Uom.IVariantUomReader? uomReader = null)
    : IRequestHandler<CreateStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        CreateStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var input = NormalizeAndValidate(request.Input);
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        input = await RetailPOS.Application.Uom.InventoryUomRules.ResolveTransferAsync(input, businessId, uomReader, cancellationToken);
        var id = await creationStore.CreateAsync(
            input,
            businessId,
            request.CreatedBy,
            cancellationToken);

        return await readStore.GetByIdAsync(id, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {id} not found");
    }

    internal static StockTransferCreateInput NormalizeAndValidate(StockTransferCreateInput input)
    {
        if (input.Items.Count == 0)
            throw new InvalidOperationException("Stock transfer must have at least one item.");

        var fromType = NormalizeLocationType(input.FromLocationType);
        var toType = NormalizeLocationType(input.ToLocationType);
        if (input.FromLocationId == input.ToLocationId && fromType == toType)
            throw new InvalidOperationException("Source and destination locations cannot be the same.");

        foreach (var item in input.Items)
        {
            var transferQuantity = ResolveTransferQuantity(item);
            if (transferQuantity <= 0)
                throw new InvalidOperationException("All transfer quantities must be greater than zero.");
        }

        return input with
        {
            FromLocationType = fromType,
            ToLocationType = toType
        };
    }

    private static string NormalizeLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }

    private static decimal ResolveTransferQuantity(StockTransferCreateItem item)
    {
        if (item.EnteredQuantity is > 0m)
            return item.EnteredQuantity.Value;
        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;
        if (item.Quantity > 0)
            return item.Quantity;
        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;
        return item.Quantity;
    }
}
