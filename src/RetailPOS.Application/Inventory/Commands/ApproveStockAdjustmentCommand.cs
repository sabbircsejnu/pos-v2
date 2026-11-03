using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record ApproveStockAdjustmentCommand(
    long AdjustmentId,
    long ApprovedBy)
    : ICommand<StockAdjustmentReadModel>;

public sealed class ApproveStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentApprovalStore approvalStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter,
    IStockAdjustmentApprovalCacheInvalidator cacheInvalidator)
    : MediatR.IRequestHandler<ApproveStockAdjustmentCommand, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        ApproveStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(cancellationToken);
        var approval = await approvalStore.ApproveAsync(
            request.AdjustmentId,
            request.ApprovedBy,
            context,
            cancellationToken);
        var adjustment = await readStore.GetByIdAsync(
                approval.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {approval.AdjustmentId} not found");

        if (adjustment.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            await cacheInvalidator.InvalidateAsync(
                adjustment.LocationId,
                adjustment.Lines.Select(line => line.VariantId).Distinct().ToArray(),
                cancellationToken);
        }

        await auditWriter.RecordApprovedAsync(
            adjustment,
            request.ApprovedBy,
            approval.JournalEntryId,
            cancellationToken);
        return adjustment;
    }
}
