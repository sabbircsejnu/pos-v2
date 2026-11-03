using RetailPOS.Application.ApReports.ReadModels;

namespace RetailPOS.Application.ApReports.Abstractions;

public interface IApPayablesReportReadStore
{
    Task<ApSubledgerReadModel> GetSubledgerAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<ApAgingReadModel> GetAgingAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierStatementReadModel> GetSupplierStatementAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<ApReconciliationReadModel> GetPayableReconciliationAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<ApReconciliationReadModel> GetAdvanceReconciliationAsync(
        ApPayablesReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}

public interface IApReportTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
