using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.Application.Barcodes.Abstractions;

public interface IBarcodePrintHistoryReadStore
{
    Task<BarcodePrintHistorySearchReadModel> SearchAsync(
        BarcodePrintHistorySearchCriteria criteria,
        long businessId,
        CancellationToken cancellationToken);

    Task<BarcodePrintHistoryLookupReadModel> GetByIdAsync(
        long id,
        BarcodePrintHistoryAccessScope accessScope,
        CancellationToken cancellationToken);
}

public interface IBarcodePrintHistoryReadScope
{
    Task<BarcodePrintHistoryAccessScope> GetAsync(CancellationToken cancellationToken);
}
