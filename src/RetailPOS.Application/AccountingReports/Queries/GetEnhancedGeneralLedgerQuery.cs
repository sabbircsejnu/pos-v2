using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetEnhancedGeneralLedgerQuery(EnhancedGeneralLedgerFilter Filter)
    : IQuery<EnhancedGeneralLedgerReadModel>;

public sealed class GetEnhancedGeneralLedgerQueryHandler(
    IEnhancedGeneralLedgerReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetEnhancedGeneralLedgerQuery, EnhancedGeneralLedgerReadModel>
{
    public async Task<EnhancedGeneralLedgerReadModel> Handle(
        GetEnhancedGeneralLedgerQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Filter.AccountId <= 0)
            throw new InvalidOperationException(
                "AccountId is required for enhanced General Ledger reporting.");

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetAsync(request.Filter, businessId, cancellationToken);
    }
}
