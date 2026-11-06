using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SalesReports.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.SalesReports;

/// <summary>Report ranges in the Company time zone: a date-only bound covers that whole local day.</summary>
internal static class SalesReportFilters
{
    public static async Task<SalesReportFilter> CreateAsync(
        ICurrentCompanyTimeZone companyTimeZone,
        DateTime? startDate,
        DateTime? endDate,
        long? outletId,
        string? paymentMethod,
        CancellationToken cancellationToken,
        int limit = 10)
    {
        var zone = await companyTimeZone.GetTimeZoneIdAsync(cancellationToken);
        var end = endDate.HasValue ? CompanyDates.EndInclusiveUtc(endDate.Value, zone) : DateTime.UtcNow;
        var start = startDate.HasValue ? CompanyDates.StartUtc(startDate.Value, zone) : end.AddDays(-30);
        return new SalesReportFilter(start, end, outletId, paymentMethod, limit, TimeZoneId: zone);
    }
}

public sealed record GetSalesReportSummaryQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId,
    string? PaymentMethod)
    : IQuery<SalesReportSummaryReadModel>;

public sealed class GetSalesReportSummaryQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetSalesReportSummaryQuery, SalesReportSummaryReadModel>
{
    public async Task<SalesReportSummaryReadModel> Handle(
        GetSalesReportSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var filter = await SalesReportFilters.CreateAsync(
            companyTimeZone,
            request.StartDate,
            request.EndDate,
            request.OutletId,
            request.PaymentMethod,
            cancellationToken);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetSummaryAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetMobileSalesDashboardQuery(DateTime? StartDate, DateTime? EndDate, long? CashierId)
    : IQuery<MobileSalesDashboardReadModel>;

public sealed class GetMobileSalesDashboardQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetMobileSalesDashboardQuery, MobileSalesDashboardReadModel>
{
    public async Task<MobileSalesDashboardReadModel> Handle(
        GetMobileSalesDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var end = request.EndDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var start = request.StartDate?.ToUniversalTime() ?? end.AddDays(-30);
        var filter = new SalesReportFilter(start, end, null, null, CashierId: request.CashierId);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetMobileSalesDashboardAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetMobileDailySalesActivityQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? CashierId)
    : IQuery<IReadOnlyList<MobileDailySalesActivityReadModel>>;

public sealed class GetMobileDailySalesActivityQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetMobileDailySalesActivityQuery, IReadOnlyList<MobileDailySalesActivityReadModel>>
{
    public async Task<IReadOnlyList<MobileDailySalesActivityReadModel>> Handle(
        GetMobileDailySalesActivityQuery request,
        CancellationToken cancellationToken)
    {
        var end = request.EndDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var start = request.StartDate?.ToUniversalTime() ?? end.AddDays(-30);
        var filter = new SalesReportFilter(start, end, null, null, CashierId: request.CashierId);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetMobileDailySalesActivityAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetTopSalesProductsQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId,
    int Limit = 10)
    : IQuery<IReadOnlyList<TopProductReadModel>>;

public sealed class GetTopSalesProductsQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetTopSalesProductsQuery, IReadOnlyList<TopProductReadModel>>
{
    public async Task<IReadOnlyList<TopProductReadModel>> Handle(
        GetTopSalesProductsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = await SalesReportFilters.CreateAsync(
            companyTimeZone,
            request.StartDate,
            request.EndDate,
            request.OutletId,
            null,
            cancellationToken,
            request.Limit);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetTopProductsAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetSalesByOutletQuery(DateTime? StartDate, DateTime? EndDate)
    : IQuery<IReadOnlyList<SalesByOutletReadModel>>;

public sealed class GetSalesByOutletQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetSalesByOutletQuery, IReadOnlyList<SalesByOutletReadModel>>
{
    public async Task<IReadOnlyList<SalesByOutletReadModel>> Handle(
        GetSalesByOutletQuery request,
        CancellationToken cancellationToken)
    {
        var filter = await SalesReportFilters.CreateAsync(
            companyTimeZone,
            request.StartDate,
            request.EndDate,
            null,
            null,
            cancellationToken);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByOutletAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetSalesByPaymentMethodQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId)
    : IQuery<IReadOnlyList<SalesByPaymentMethodReadModel>>;

public sealed class GetSalesByPaymentMethodQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetSalesByPaymentMethodQuery, IReadOnlyList<SalesByPaymentMethodReadModel>>
{
    public async Task<IReadOnlyList<SalesByPaymentMethodReadModel>> Handle(
        GetSalesByPaymentMethodQuery request,
        CancellationToken cancellationToken)
    {
        var filter = await SalesReportFilters.CreateAsync(
            companyTimeZone,
            request.StartDate,
            request.EndDate,
            request.OutletId,
            null,
            cancellationToken);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByPaymentMethodAsync(scope, filter, cancellationToken);
    }
}

public sealed record GetDailySalesTrendQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId)
    : IQuery<IReadOnlyList<DailySalesTrendReadModel>>;

public sealed class GetDailySalesTrendQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetDailySalesTrendQuery, IReadOnlyList<DailySalesTrendReadModel>>
{
    public async Task<IReadOnlyList<DailySalesTrendReadModel>> Handle(
        GetDailySalesTrendQuery request,
        CancellationToken cancellationToken)
    {
        var filter = await SalesReportFilters.CreateAsync(
            companyTimeZone,
            request.StartDate,
            request.EndDate,
            request.OutletId,
            null,
            cancellationToken);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetDailyTrendAsync(scope, filter, cancellationToken);
    }
}
