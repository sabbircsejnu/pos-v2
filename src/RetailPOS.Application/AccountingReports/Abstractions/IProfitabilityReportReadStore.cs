using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IProfitabilityReportReadStore
{
    Task<BusinessProfitabilityReadModel> GetBusinessAsync(
        ProfitabilityFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<OutletProfitabilityReadModel> GetOutletsAsync(
        DateTime dateFrom,
        DateTime dateTo,
        long businessId,
        CancellationToken cancellationToken);
}
