using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Abstractions;

public interface IUserReadStore
{
    Task<UserReadModel?> FindByIdAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserReadModel>> ListByOutletAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserReadModel>> ListByRoleAsync(
        long roleId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<UserListReadModel> SearchAsync(
        int pageNumber,
        int pageSize,
        string? search,
        long? roleId,
        long? outletId,
        bool? isActive,
        long? businessId,
        CancellationToken cancellationToken);
}
