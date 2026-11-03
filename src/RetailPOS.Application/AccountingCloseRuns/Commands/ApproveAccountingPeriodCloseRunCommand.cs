using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record ApproveAccountingPeriodCloseRunCommand(
    long AccountingPeriodCloseRunId,
    long ActorUserId) : IRequest;

public sealed class ApproveAccountingPeriodCloseRunCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ApproveAccountingPeriodCloseRunCommand>
{
    public async Task Handle(
        ApproveAccountingPeriodCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ApproveAsync(
            businessId,
            request.AccountingPeriodCloseRunId,
            request.ActorUserId,
            cancellationToken);
    }
}
