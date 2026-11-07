using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record UpdateStockTransferCommand(
    long Id,
    StockTransferCreateInput Input)
    : ICommand<StockTransferReadModel>;

public sealed class UpdateStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferDraftStore draftStore,
    IStockTransferReadStore readStore,
    RetailPOS.Application.Uom.IVariantUomReader? uomReader = null)
    : IRequestHandler<UpdateStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        UpdateStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var existing = await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {request.Id} not found");
        if (!existing.Status.Equals(StockTransfer.StatusDraft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot edit transfer in status '{existing.Status}'. Only draft transfers are editable.");
        }

        var input = await RetailPOS.Application.Uom.InventoryUomRules.ResolveTransferAsync(
            CreateStockTransferCommandHandler.NormalizeAndValidate(request.Input), businessId, uomReader, cancellationToken);
        var updatedId = await draftStore.UpdateDraftAsync(
            request.Id,
            input,
            businessId,
            cancellationToken);

        return await readStore.GetByIdAsync(updatedId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {updatedId} not found");
    }
}
