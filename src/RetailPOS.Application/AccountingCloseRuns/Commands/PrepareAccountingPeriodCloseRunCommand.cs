using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;

namespace RetailPOS.Application.AccountingCloseRuns.Commands;

public sealed record PrepareAccountingPeriodCloseRunCommand(
    long AccountingPeriodId,
    long ActorUserId) : IRequest<long>;

public sealed class PrepareAccountingPeriodCloseRunCommandHandler(
    IAccountingPeriodCloseRunCommandStore commandStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<PrepareAccountingPeriodCloseRunCommand, long>
{
    public async Task<long> Handle(
        PrepareAccountingPeriodCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.PrepareAsync(
            businessId,
            request.AccountingPeriodId,
            request.ActorUserId,
            cancellationToken);
    }
}
