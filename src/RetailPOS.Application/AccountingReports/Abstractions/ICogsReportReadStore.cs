using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface ICogsReportReadStore
{
    Task<CogsReportReadModel> GetAsync(
        CogsReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
