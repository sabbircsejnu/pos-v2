using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Identity;

public sealed class UserOutletAccessStore(RetailPOSDbContext dbContext) : IUserOutletAccessStore
{
    public Task<UserLocationAccessUser?> FindUserAsync(long userId) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserLocationAccessUser(
                user.BusinessId,
                user.OutletId,
                user.InventoryLocationAccessScope,
                user.ContextSwitchEnabled,
                user.ContextLocationScope))
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<AuthorizedLocationReadModel>> ListOutletsAsync(long? businessId)
    {
        var query = dbContext.Outlets.AsNoTracking();
        if (businessId.HasValue)
        {
            query = query.Where(outlet => outlet.BusinessId == businessId.Value);
        }

        return await query
            .OrderBy(outlet => outlet.Name)
            .Select(outlet => new AuthorizedLocationReadModel(outlet.Id, outlet.Name, "outlet"))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AuthorizedLocationReadModel>> ListWarehousesAsync(long? businessId)
    {
        var query = dbContext.Warehouses.AsNoTracking();
        if (businessId.HasValue)
        {
            query = query.Where(warehouse => warehouse.BusinessId == businessId.Value);
        }

        return await query
            .OrderBy(warehouse => warehouse.Name)
            .Select(warehouse => new AuthorizedLocationReadModel(warehouse.Id, warehouse.Name, "warehouse"))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UserOutletAssignmentReadModel>> ListActiveOutletAssignmentsAsync(long userId)
    {
        var assignments = await dbContext.UserOutletAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive)
            .Include(assignment => assignment.Outlet)
            .ToListAsync();

        return assignments
            .Select(assignment => new UserOutletAssignmentReadModel(
                assignment.Id,
                assignment.OutletId,
                assignment.IsPrimary,
                assignment.Outlet?.Name,
                assignment.Outlet?.BusinessId))
            .ToArray();
    }

    public async Task<IReadOnlyList<UserWarehouseAssignmentReadModel>> ListActiveWarehouseAssignmentsAsync(long userId)
    {
        var assignments = await dbContext.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive)
            .Include(assignment => assignment.Warehouse)
            .ToListAsync();

        return assignments
            .Select(assignment => new UserWarehouseAssignmentReadModel(
                assignment.Id,
                assignment.WarehouseId,
                assignment.IsPrimary,
                assignment.Warehouse?.Name,
                assignment.Warehouse?.BusinessId))
            .ToArray();
    }
}
