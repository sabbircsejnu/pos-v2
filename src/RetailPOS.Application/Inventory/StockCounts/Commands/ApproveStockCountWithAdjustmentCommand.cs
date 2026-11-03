using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

/// <summary>
/// StockCount approval that atomically persists the approval decision, a durable adjustment job and its
/// transactional outbox message. The response means Approved / AdjustmentPending, never Adjusted.
/// </summary>
public sealed record ApproveStockCountWithAdjustmentCommand(ApproveStockCountWithAdjustmentInput Input)
    : IRequest<ApproveStockCountWithAdjustmentResult>;

public sealed class ApproveStockCountWithAdjustmentCommandHandler(
    IStockCountAdjustmentJobStore store)
    : IRequestHandler<ApproveStockCountWithAdjustmentCommand, ApproveStockCountWithAdjustmentResult>
{
    public Task<ApproveStockCountWithAdjustmentResult> Handle(
        ApproveStockCountWithAdjustmentCommand request,
        CancellationToken cancellationToken) =>
        store.ApproveAndEnqueueAsync(request.Input, cancellationToken);
}
