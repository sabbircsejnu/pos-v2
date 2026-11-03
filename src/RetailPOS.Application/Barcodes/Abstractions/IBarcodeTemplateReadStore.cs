using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.Application.Barcodes.Abstractions;

public interface IBarcodeTemplateReadStore
{
    Task<IReadOnlyList<BarcodeTemplateReadModel>> GetByBusinessAsync(
        long businessId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<BarcodeTemplateLookupReadModel> GetByIdAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        CancellationToken cancellationToken);
}

public interface IBarcodeTemplateDefaultsProvisioner
{
    Task EnsureAsync(long businessId, CancellationToken cancellationToken);
}

public interface IBarcodeTemplateReadScope
{
    Task<BarcodeTemplateAccessScope> GetAsync(CancellationToken cancellationToken);
}
