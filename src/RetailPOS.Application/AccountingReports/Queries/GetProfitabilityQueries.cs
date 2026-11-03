using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetBusinessProfitabilityQuery(ProfitabilityFilter Filter)
    : IQuery<BusinessProfitabilityReadModel>;

public sealed class GetBusinessProfitabilityQueryHandler(
    IProfitabilityReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetBusinessProfitabilityQuery, BusinessProfitabilityReadModel>
{
    public async Task<BusinessProfitabilityReadModel> Handle(
        GetBusinessProfitabilityQuery request,
        CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = RequireDates(request.Filter);
        ValidateLocationType(request.Filter.LocationType);
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetBusinessAsync(
            request.Filter with { DateFrom = dateFrom, DateTo = dateTo },
            businessId,
            cancellationToken);
    }

    internal static (DateTime DateFrom, DateTime DateTo) RequireDates(
        ProfitabilityFilter filter)
    {
        if (!filter.DateFrom.HasValue || !filter.DateTo.HasValue)
            throw new InvalidOperationException(
                "DateFrom and DateTo are required for profitability reporting.");
        if (filter.DateTo.Value.Date < filter.DateFrom.Value.Date)
            throw new InvalidOperationException(
                "DateTo must not be earlier than DateFrom.");
        return (filter.DateFrom.Value.Date, filter.DateTo.Value.Date);
    }

    internal static void ValidateLocationType(string? locationType)
    {
        if (!string.IsNullOrWhiteSpace(locationType)
            && !string.Equals(locationType, "outlet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationType, "warehouse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "LocationType must be either outlet or warehouse.");
    }
}

public sealed record GetOutletProfitabilityQuery(ProfitabilityFilter Filter)
    : IQuery<OutletProfitabilityReadModel>;

public sealed class GetOutletProfitabilityQueryHandler(
    IProfitabilityReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetOutletProfitabilityQuery, OutletProfitabilityReadModel>
{
    public async Task<OutletProfitabilityReadModel> Handle(
        GetOutletProfitabilityQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var (dateFrom, dateTo) =
            GetBusinessProfitabilityQueryHandler.RequireDates(request.Filter);
        return await readStore.GetOutletsAsync(
            dateFrom,
            dateTo,
            businessId,
            cancellationToken);
    }
}

public sealed record GetComparativeBusinessProfitabilityQuery(ProfitabilityFilter Filter)
    : IQuery<ComparativeBusinessProfitabilityReadModel>;

public sealed class GetComparativeBusinessProfitabilityQueryHandler(ISender sender)
    : IRequestHandler<GetComparativeBusinessProfitabilityQuery, ComparativeBusinessProfitabilityReadModel>
{
    public async Task<ComparativeBusinessProfitabilityReadModel> Handle(
        GetComparativeBusinessProfitabilityQuery request,
        CancellationToken cancellationToken)
    {
        RequireComparativeDates(request.Filter, "Business profitability");
        var current = await sender.Send(
            new GetBusinessProfitabilityQuery(request.Filter),
            cancellationToken);
        var comparison = await sender.Send(
            new GetBusinessProfitabilityQuery(ComparisonFilter(request.Filter)),
            cancellationToken);
        return new ComparativeBusinessProfitabilityReadModel(current, comparison);
    }

    internal static void RequireComparativeDates(
        ProfitabilityFilter filter,
        string reportName)
    {
        _ = GetBusinessProfitabilityQueryHandler.RequireDates(filter);
        if (!filter.ComparisonDateFrom.HasValue
            || !filter.ComparisonDateTo.HasValue)
            throw new InvalidOperationException(
                $"Current and comparison date ranges are required for comparative {reportName} reporting.");
    }

    internal static ProfitabilityFilter ComparisonFilter(ProfitabilityFilter filter) =>
        new(
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo,
            null,
            null,
            filter.LocationId,
            filter.LocationType);
}

public sealed record GetComparativeOutletProfitabilityQuery(ProfitabilityFilter Filter)
    : IQuery<ComparativeOutletProfitabilityReadModel>;

public sealed class GetComparativeOutletProfitabilityQueryHandler(ISender sender)
    : IRequestHandler<GetComparativeOutletProfitabilityQuery, ComparativeOutletProfitabilityReadModel>
{
    public async Task<ComparativeOutletProfitabilityReadModel> Handle(
        GetComparativeOutletProfitabilityQuery request,
        CancellationToken cancellationToken)
    {
        GetComparativeBusinessProfitabilityQueryHandler.RequireComparativeDates(
            request.Filter,
            "Outlet profitability");
        var current = await sender.Send(
            new GetOutletProfitabilityQuery(request.Filter),
            cancellationToken);
        var comparison = await sender.Send(
            new GetOutletProfitabilityQuery(
                GetComparativeBusinessProfitabilityQueryHandler.ComparisonFilter(
                    request.Filter)),
            cancellationToken);
        return new ComparativeOutletProfitabilityReadModel(current, comparison);
    }
}
