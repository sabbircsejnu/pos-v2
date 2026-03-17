using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for sales reporting queries
/// </summary>
public interface ISalesReportService
{
    /// <summary>Returns aggregated sales summary for the given filter</summary>
    Task<SalesReportDto> GetSummaryAsync(SalesReportFilterDto filter);

    /// <summary>Returns top-selling product variants</summary>
    Task<List<TopProductDto>> GetTopProductsAsync(SalesReportFilterDto filter, int limit = 10);

    /// <summary>Returns sales grouped by outlet</summary>
    Task<List<SalesByOutletDto>> GetSalesByOutletAsync(SalesReportFilterDto filter);

    /// <summary>Returns sales grouped by payment method</summary>
    Task<List<SalesByPaymentMethodDto>> GetSalesByPaymentMethodAsync(SalesReportFilterDto filter);

    /// <summary>Returns daily sales trend for the period</summary>
    Task<List<DailySalesTrendDto>> GetDailySalesTrendAsync(SalesReportFilterDto filter);
}
