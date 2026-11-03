using RetailPOS.Application.VatReports.ReadModels;

namespace RetailPOS.Application.VatReports.Abstractions;

public interface IVatReportReadStore
{
    Task<IReadOnlyList<VatTransactionReadModel>> GetTransactionsAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<VatSummaryReadModel> GetSummaryAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<VatReconciliationReadModel> GetReconciliationAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VatExceptionReadModel>> GetExceptionsAsync(
        VatReportFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
