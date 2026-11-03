using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Abstractions;

public interface IRoleReadStore
{
    Task<RoleReadModel?> FindByIdAsync(
        long roleId,
        bool isSuperAdmin,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleReadModel>> ListAsync(
        bool isSuperAdmin,
        long? businessId,
        CancellationToken cancellationToken);
}
