using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IEnhancedGeneralLedgerReadStore
{
    Task<EnhancedGeneralLedgerReadModel> GetAsync(
        EnhancedGeneralLedgerFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
