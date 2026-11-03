using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;

namespace RetailPOS.Application.Barcodes.Abstractions;

public interface IBarcodeTemplateWriteStore
{
    Task<BarcodeTemplateReadModel> CreateAsync(
        BarcodeTemplateWriteModel template,
        long businessId,
        long? userId,
        CancellationToken cancellationToken);

    Task<BarcodeTemplateReadModel> UpdateAsync(
        long id,
        BarcodeTemplateWriteModel template,
        BarcodeTemplateAccessScope accessScope,
        long? userId,
        CancellationToken cancellationToken);

    Task SetDefaultAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        long? userId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        CancellationToken cancellationToken);
}

public interface IBarcodeTemplateWriteScope
{
    Task<BarcodeTemplateAccessScope> GetAsync(CancellationToken cancellationToken);
}
