using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;

namespace RetailPOS.Application.Barcodes.Abstractions;

public interface IBarcodePrintHistoryWriteStore
{
    Task<BarcodePrintHistoryReadModel> CreateAsync(
        BarcodePrintHistoryWriteModel history,
        long businessId,
        bool isSuperAdmin,
        CancellationToken cancellationToken);
}

public interface IBarcodePrintHistoryWriteScope
{
    Task<BarcodePrintHistoryAccessScope> GetAsync(CancellationToken cancellationToken);
}
