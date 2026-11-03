using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.Application.FinancialYearCloseRuns.Commands;

public sealed record ReopenAccountingFinancialYearCloseRunCommand(
    long Id,
    string? Reason,
    DateTime PostingDate,
    long ActorUserId) : IRequest;

public sealed class ReopenAccountingFinancialYearCloseRunCommandHandler(
    IAccountingFinancialYearCloseRunCommandStore commandStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<ReopenAccountingFinancialYearCloseRunCommand>
{
    public async Task Handle(
        ReopenAccountingFinancialYearCloseRunCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await commandStore.ReopenAsync(
            businessId,
            request.Id,
            request.Reason,
            request.PostingDate,
            request.ActorUserId,
            cancellationToken);
    }
}
