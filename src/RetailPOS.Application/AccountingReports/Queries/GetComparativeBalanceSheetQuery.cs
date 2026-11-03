using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetComparativeBalanceSheetQuery(
    BalanceSheetReportFilter Filter,
    DateTime? ComparisonAsOfDate)
    : IQuery<ComparativeBalanceSheetReadModel>;

public sealed class GetComparativeBalanceSheetQueryHandler(
    IBalanceSheetReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetComparativeBalanceSheetQuery, ComparativeBalanceSheetReadModel>
{
    public async Task<ComparativeBalanceSheetReadModel> Handle(
        GetComparativeBalanceSheetQuery request,
        CancellationToken cancellationToken)
    {
        if (!request.ComparisonAsOfDate.HasValue)
            throw new InvalidOperationException(
                "ComparisonAsOfDate is required for comparative Balance Sheet reporting.");

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var current = await store.GetBalanceSheetAsync(
            request.Filter,
            businessId,
            cancellationToken);
        var comparison = await store.GetBalanceSheetAsync(
            request.Filter with { AsOfDate = request.ComparisonAsOfDate },
            businessId,
            cancellationToken);
        return new ComparativeBalanceSheetReadModel(current, comparison);
    }
}
