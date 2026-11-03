namespace RetailPOS.Application.Identity.LocationAccess;

public interface IUserOutletAccessStore
{
    Task<UserLocationAccessUser?> FindUserAsync(long userId);

    Task<IReadOnlyList<AuthorizedLocationReadModel>> ListOutletsAsync(long? businessId);

    Task<IReadOnlyList<AuthorizedLocationReadModel>> ListWarehousesAsync(long? businessId);

    Task<IReadOnlyList<UserOutletAssignmentReadModel>> ListActiveOutletAssignmentsAsync(long userId);

    Task<IReadOnlyList<UserWarehouseAssignmentReadModel>> ListActiveWarehouseAssignmentsAsync(long userId);
}
