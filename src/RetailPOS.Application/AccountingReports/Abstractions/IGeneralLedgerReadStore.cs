using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IGeneralLedgerReadStore
{
    Task<GeneralLedgerReportReadModel> GetGeneralLedgerAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<TrialBalanceReportReadModel> GetTrialBalanceAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<ProfitLossReportReadModel> GetProfitLossAsync(
        GeneralLedgerReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<ProfitLossReportReadModel> GetHistoricalProfitLossAsync(
        DateTime dateFrom,
        DateTime dateTo,
        long? locationId,
        string? locationType,
        long businessId,
        CancellationToken cancellationToken);
}

public interface IAccountingReportTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
