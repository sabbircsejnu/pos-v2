using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetBalanceSheetQuery(BalanceSheetReportFilter Filter)
    : IQuery<BalanceSheetReportReadModel>;

public sealed class GetBalanceSheetQueryHandler(
    IBalanceSheetReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetBalanceSheetQuery, BalanceSheetReportReadModel>
{
    public async Task<BalanceSheetReportReadModel> Handle(
        GetBalanceSheetQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetBalanceSheetAsync(request.Filter, businessId, cancellationToken);
    }
}
