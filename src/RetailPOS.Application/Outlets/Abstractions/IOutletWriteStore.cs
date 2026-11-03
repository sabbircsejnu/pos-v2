namespace RetailPOS.Application.Outlets.Abstractions;

public interface IOutletWriteStore
{
    /// <summary>Creates the outlet together with its POS setup (invoice prefix, time zone, terminals) in one save.</summary>
    Task<OutletReadModel> CreateAsync(
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        long? businessId,
        OutletPosSetupInput posSetup,
        CancellationToken cancellationToken);

    Task<OutletReadModel> UpdateAsync(
        long outletId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        long? businessId,
        OutletPosSetupInput posSetup,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken);

    /// <summary>Deletes a terminal that has no sales, sessions or receipt history.</summary>
    Task DeleteTerminalAsync(
        long outletId,
        long terminalId,
        long? businessId,
        CancellationToken cancellationToken);
}
