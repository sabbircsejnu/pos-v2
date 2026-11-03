using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SalesReports.Abstractions;

namespace RetailPOS.Application.SalesReports;

public sealed record GetSalesReportSummaryQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId,
    string? PaymentMethod)
    : IQuery<SalesReportSummaryReadModel>;

public sealed class GetSalesReportSummaryQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetSalesReportSummaryQuery, SalesReportSummaryReadModel>
{
    public async Task<SalesReportSummaryReadModel> Handle(
        GetSalesReportSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var filter = CreateFilter(request.StartDate, request.EndDate, request.OutletId, request.PaymentMethod);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetSummaryAsync(scope, filter, cancellationToken);
    }

    private static SalesReportFilter CreateFilter(
        DateTime? startDate,
        DateTime? endDate,
        long? outletId,
        string? paymentMethod)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        return new SalesReportFilter(
            startDate?.ToUniversalTime() ?? end.AddDays(-30),
            end,
            outletId,
            paymentMethod);
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
    ISalesReportReadStore store)
    : IRequestHandler<GetTopSalesProductsQuery, IReadOnlyList<TopProductReadModel>>
{
    public async Task<IReadOnlyList<TopProductReadModel>> Handle(
        GetTopSalesProductsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = CreateFilter(request.StartDate, request.EndDate, request.OutletId, request.Limit);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetTopProductsAsync(scope, filter, cancellationToken);
    }

    private static SalesReportFilter CreateFilter(
        DateTime? startDate,
        DateTime? endDate,
        long? outletId,
        int limit)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        return new SalesReportFilter(
            startDate?.ToUniversalTime() ?? end.AddDays(-30),
            end,
            outletId,
            null,
            limit);
    }
}

public sealed record GetSalesByOutletQuery(DateTime? StartDate, DateTime? EndDate)
    : IQuery<IReadOnlyList<SalesByOutletReadModel>>;

public sealed class GetSalesByOutletQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetSalesByOutletQuery, IReadOnlyList<SalesByOutletReadModel>>
{
    public async Task<IReadOnlyList<SalesByOutletReadModel>> Handle(
        GetSalesByOutletQuery request,
        CancellationToken cancellationToken)
    {
        var filter = CreateFilter(request.StartDate, request.EndDate);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByOutletAsync(scope, filter, cancellationToken);
    }

    private static SalesReportFilter CreateFilter(DateTime? startDate, DateTime? endDate)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        return new SalesReportFilter(startDate?.ToUniversalTime() ?? end.AddDays(-30), end, null, null);
    }
}

public sealed record GetSalesByPaymentMethodQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId)
    : IQuery<IReadOnlyList<SalesByPaymentMethodReadModel>>;

public sealed class GetSalesByPaymentMethodQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetSalesByPaymentMethodQuery, IReadOnlyList<SalesByPaymentMethodReadModel>>
{
    public async Task<IReadOnlyList<SalesByPaymentMethodReadModel>> Handle(
        GetSalesByPaymentMethodQuery request,
        CancellationToken cancellationToken)
    {
        var filter = CreateFilter(request.StartDate, request.EndDate, request.OutletId);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByPaymentMethodAsync(scope, filter, cancellationToken);
    }

    private static SalesReportFilter CreateFilter(
        DateTime? startDate,
        DateTime? endDate,
        long? outletId)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        return new SalesReportFilter(startDate?.ToUniversalTime() ?? end.AddDays(-30), end, outletId, null);
    }
}

public sealed record GetDailySalesTrendQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    long? OutletId)
    : IQuery<IReadOnlyList<DailySalesTrendReadModel>>;

public sealed class GetDailySalesTrendQueryHandler(
    ISalesReportReadScopeProvider scopeProvider,
    ISalesReportReadStore store)
    : IRequestHandler<GetDailySalesTrendQuery, IReadOnlyList<DailySalesTrendReadModel>>
{
    public async Task<IReadOnlyList<DailySalesTrendReadModel>> Handle(
        GetDailySalesTrendQuery request,
        CancellationToken cancellationToken)
    {
        var end = request.EndDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var filter = new SalesReportFilter(
            request.StartDate?.ToUniversalTime() ?? end.AddDays(-30),
            end,
            request.OutletId,
            null);
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetDailyTrendAsync(scope, filter, cancellationToken);
    }
}
