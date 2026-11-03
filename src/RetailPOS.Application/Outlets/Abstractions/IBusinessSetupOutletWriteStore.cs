namespace RetailPOS.Application.Outlets.Abstractions;

public interface IBusinessSetupOutletWriteStore
{
    Task<OutletReadModel> CreateForBusinessAsync(
        long businessId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        CancellationToken cancellationToken);

    Task<OutletReadModel> UpdateForBusinessAsync(
        long businessId,
        long outletId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        CancellationToken cancellationToken);

    Task<bool> DeleteForBusinessAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken);
}
