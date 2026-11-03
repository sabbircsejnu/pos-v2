using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.Application.FinancialYearCloseRuns.Commands;

public sealed record ApproveAccountingFinancialYearCloseRunCommand(
    long Id,
    long ActorUserId) : IRequest;

public sealed class ApproveAccountingFinancialYearCloseRunCommandHandler(
    IAccountingFinancialYearCloseRunCommandStore commandStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<ApproveAccountingFinancialYearCloseRunCommand>
{
    public async Task Handle(
        ApproveAccountingFinancialYearCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ApproveAsync(
            businessId,
            request.Id,
            request.ActorUserId,
            cancellationToken);
    }
}
