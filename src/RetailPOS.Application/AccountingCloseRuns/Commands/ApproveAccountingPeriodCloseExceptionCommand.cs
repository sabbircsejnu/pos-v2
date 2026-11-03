using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record ApproveAccountingPeriodCloseExceptionCommand(
    long AccountingPeriodCloseRunId,
    long GateSnapshotId,
    string? Reason,
    long ActorUserId) : IRequest;

public sealed class ApproveAccountingPeriodCloseExceptionCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ApproveAccountingPeriodCloseExceptionCommand>
{
    public async Task Handle(
        ApproveAccountingPeriodCloseExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ApproveExceptionAsync(
            businessId,
            request.AccountingPeriodCloseRunId,
            request.GateSnapshotId,
            request.Reason,
            request.ActorUserId,
            cancellationToken);
    }
}
