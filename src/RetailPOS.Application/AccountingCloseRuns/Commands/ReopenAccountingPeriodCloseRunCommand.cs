using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record ReopenAccountingPeriodCloseRunCommand(
    long AccountingPeriodCloseRunId,
    string? Reason,
    long ActorUserId) : IRequest;

public sealed class ReopenAccountingPeriodCloseRunCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ReopenAccountingPeriodCloseRunCommand>
{
    public async Task Handle(
        ReopenAccountingPeriodCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ReopenAsync(
            businessId,
            request.AccountingPeriodCloseRunId,
            request.Reason,
            request.ActorUserId,
            cancellationToken);
    }
}
