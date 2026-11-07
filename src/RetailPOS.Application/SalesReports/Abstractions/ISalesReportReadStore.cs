namespace RetailPOS.Application.SalesReports.Abstractions;

public interface ISalesReportReadStore
{
    Task<MobileSalesDashboardReadModel> GetMobileSalesDashboardAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MobileDailySalesActivityReadModel>> GetMobileDailySalesActivityAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<SalesReportSummaryReadModel> GetSummaryAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<decimal> GetUnitCountAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TopProductReadModel>> GetTopProductsAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesByOutletReadModel>> GetByOutletAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesByPaymentMethodReadModel>> GetByPaymentMethodAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DailySalesTrendReadModel>> GetDailyTrendAsync(
        SalesReportReadScope scope,
        SalesReportFilter filter,
        CancellationToken cancellationToken);
}

public interface ISalesReportReadScopeProvider
{
    Task<SalesReportReadScope> GetAsync(CancellationToken cancellationToken);
}
