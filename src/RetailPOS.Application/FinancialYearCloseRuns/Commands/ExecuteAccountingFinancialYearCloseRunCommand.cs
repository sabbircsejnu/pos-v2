using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.Application.FinancialYearCloseRuns.Commands;

public sealed record ExecuteAccountingFinancialYearCloseRunCommand(
    long Id,
    long ActorUserId) : IRequest;

public sealed class ExecuteAccountingFinancialYearCloseRunCommandHandler(
    IAccountingFinancialYearCloseRunCommandStore commandStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<ExecuteAccountingFinancialYearCloseRunCommand>
{
    public async Task Handle(
        ExecuteAccountingFinancialYearCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ExecuteAsync(
            businessId,
            request.Id,
            request.ActorUserId,
            cancellationToken);
    }
}
