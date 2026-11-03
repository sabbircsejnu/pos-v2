namespace RetailPOS.Application.Barcodes.Abstractions;

public interface IBarcodeCompanyNameReadStore
{
    Task<string?> FindBusinessNameAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<long?> FindUserOutletIdAsync(
        long userId,
        CancellationToken cancellationToken);

    Task<string?> FindOutletNameAsync(
        long outletId,
        CancellationToken cancellationToken);
}

public interface IBarcodeTenantScope
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
