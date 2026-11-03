using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record ReevaluateAccountingPeriodCloseRunCommand(
    long AccountingPeriodCloseRunId,
    long ActorUserId) : IRequest;

public sealed class ReevaluateAccountingPeriodCloseRunCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ReevaluateAccountingPeriodCloseRunCommand>
{
    public async Task Handle(
        ReevaluateAccountingPeriodCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ReevaluateAsync(
            businessId,
            request.AccountingPeriodCloseRunId,
            request.ActorUserId,
            cancellationToken);
    }
}
