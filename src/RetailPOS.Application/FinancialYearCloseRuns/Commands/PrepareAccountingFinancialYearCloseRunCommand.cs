using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.Application.FinancialYearCloseRuns.Commands;

public sealed record PrepareAccountingFinancialYearCloseRunCommand(
    long FinancialYearId,
    long ActorUserId) : IRequest<long>;

public sealed class PrepareAccountingFinancialYearCloseRunCommandHandler(
    IAccountingFinancialYearCloseRunCommandStore commandStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<PrepareAccountingFinancialYearCloseRunCommand, long>
{
    public async Task<long> Handle(
        PrepareAccountingFinancialYearCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.PrepareAsync(
            businessId,
            request.FinancialYearId,
            request.ActorUserId,
            cancellationToken);
    }
}
