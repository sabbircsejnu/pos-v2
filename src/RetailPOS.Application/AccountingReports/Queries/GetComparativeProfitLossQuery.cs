using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetComparativeProfitLossQuery(ComparativeProfitLossReportFilter Filter)
    : IQuery<ComparativeProfitLossReadModel>;

public sealed class GetComparativeProfitLossQueryHandler(
    IGeneralLedgerReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetComparativeProfitLossQuery, ComparativeProfitLossReadModel>
{
    public async Task<ComparativeProfitLossReadModel> Handle(
        GetComparativeProfitLossQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        if (!filter.DateFrom.HasValue
            || !filter.DateTo.HasValue
            || !filter.ComparisonDateFrom.HasValue
            || !filter.ComparisonDateTo.HasValue)
            throw new InvalidOperationException(
                "Current and comparison date ranges are required for comparative Profit and Loss reporting.");

        ValidateLocationType(filter.LocationType);
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var current = await store.GetHistoricalProfitLossAsync(
            filter.DateFrom.Value,
            filter.DateTo.Value,
            filter.LocationId,
            filter.LocationType,
            businessId,
            cancellationToken);
        var comparison = await store.GetHistoricalProfitLossAsync(
            filter.ComparisonDateFrom.Value,
            filter.ComparisonDateTo.Value,
            filter.LocationId,
            filter.LocationType,
            businessId,
            cancellationToken);
        return new ComparativeProfitLossReadModel(current, comparison);
    }

    private static void ValidateLocationType(string? locationType)
    {
        if (!string.IsNullOrWhiteSpace(locationType)
            && !string.Equals(locationType, "outlet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationType, "warehouse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LocationType must be either outlet or warehouse.");
    }
}
