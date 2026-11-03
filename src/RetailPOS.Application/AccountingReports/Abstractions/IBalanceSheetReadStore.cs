using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IBalanceSheetReadStore
{
    Task<BalanceSheetReportReadModel> GetBalanceSheetAsync(
        BalanceSheetReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
