using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record ExecuteAccountingPeriodCloseRunCommand(
    long AccountingPeriodCloseRunId,
    string? Reason,
    long ActorUserId) : IRequest<bool>;

public sealed class ExecuteAccountingPeriodCloseRunCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ExecuteAccountingPeriodCloseRunCommand, bool>
{
    public async Task<bool> Handle(
        ExecuteAccountingPeriodCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ExecuteAsync(
            businessId,
            request.AccountingPeriodCloseRunId,
            request.Reason,
            request.ActorUserId,
            cancellationToken);
    }
}
