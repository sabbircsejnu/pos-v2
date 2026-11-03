using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record SubmitStockRequisitionCommand(long Id, long SubmittedBy)
    : ICommand<StockRequisitionReadModel>;

public sealed class SubmitStockRequisitionCommandHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionLifecycleStore lifecycleStore,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<SubmitStockRequisitionCommand, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        SubmitStockRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        await lifecycleStore.SubmitAsync(
            request.Id,
            businessId,
            request.SubmittedBy,
            cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Updated stock requisition {request.Id} could not be read in its Business scope.");
    }
}

public sealed record ApproveStockRequisitionCommand(long Id, long ApprovedBy)
    : ICommand<StockRequisitionReadModel>;

public sealed class ApproveStockRequisitionCommandHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionLifecycleStore lifecycleStore,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<ApproveStockRequisitionCommand, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        ApproveStockRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        await lifecycleStore.ApproveAsync(
            request.Id,
            businessId,
            request.ApprovedBy,
            cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Updated stock requisition {request.Id} could not be read in its Business scope.");
    }
}

public sealed record RejectStockRequisitionCommand(
    long Id,
    long RejectedBy,
    string? Reason)
    : ICommand<StockRequisitionReadModel>;

public sealed class RejectStockRequisitionCommandHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionLifecycleStore lifecycleStore,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<RejectStockRequisitionCommand, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        RejectStockRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        await lifecycleStore.RejectAsync(
            request.Id,
            businessId,
            request.RejectedBy,
            request.Reason,
            cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Updated stock requisition {request.Id} could not be read in its Business scope.");
    }
}
