using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Outlets;

public sealed class OutletReadStore(RetailPOSDbContext dbContext) : IOutletReadStore
{
    public async Task<IReadOnlyList<OutletReadModel>> GetAllAsync(
        long? businessId,
        CancellationToken cancellationToken) =>
        await OutletPosSetupApplier.AttachAsync(
            dbContext,
            await Project(GetOutletsQuery(businessId)
                .OrderBy(outlet => outlet.Name), businessId)
                .ToListAsync(cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<OutletReadModel>> SearchAsync(
        string searchTerm,
        long? businessId,
        CancellationToken cancellationToken) =>
        await OutletPosSetupApplier.AttachAsync(
            dbContext,
            await Project(GetOutletsQuery(businessId)
                .Where(outlet =>
                    outlet.Name.Contains(searchTerm)
                    || outlet.Address.Contains(searchTerm)
                    || (outlet.ContactNumber != null && outlet.ContactNumber.Contains(searchTerm)))
                .OrderBy(outlet => outlet.Name), businessId)
                .ToListAsync(cancellationToken),
            cancellationToken);

    public async Task<OutletReadModel?> GetByIdAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var outlet = await Project(GetOutletsQuery(businessId).Where(candidate => candidate.Id == outletId), businessId)
            .FirstOrDefaultAsync(cancellationToken);
        return outlet is null
            ? null
            : (await OutletPosSetupApplier.AttachAsync(dbContext, [outlet], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<OutletReadModel>> GetByBusinessIdAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await OutletPosSetupApplier.AttachAsync(
            dbContext,
            await Project(GetOutletsQuery(businessId)
                .OrderBy(outlet => outlet.Name), businessId)
                .ToListAsync(cancellationToken),
            cancellationToken);

    public async Task<OutletStatsReadModel?> GetStatsAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var outlet = await Project(
                GetOutletsQuery(businessId).Where(candidate => candidate.Id == outletId), businessId)
            .FirstOrDefaultAsync(cancellationToken);
        return outlet is null
            ? null
            : new OutletStatsReadModel(outlet.Id, outlet.Name, outlet.UserCount, 0, 0);
    }

    private IQueryable<Outlet> GetOutletsQuery(long? businessId) =>
        dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => !businessId.HasValue || outlet.BusinessId == businessId.Value);

    private IQueryable<OutletReadModel> Project(IQueryable<Outlet> outlets, long? businessId) =>
        outlets.Select(outlet => new OutletReadModel(
                outlet.Id,
                outlet.Name,
                outlet.Address,
                outlet.ContactNumber,
                outlet.ManagerId,
                outlet.Manager == null ? null : outlet.Manager.Name,
                dbContext.Users.Count(user =>
                    user.OutletId == outlet.Id
                    && user.IsActive
                    && (!businessId.HasValue || user.BusinessId == businessId.Value)),
                outlet.CreatedAt,
                outlet.UpdatedAt,
                null,
                false,
                null,
                null));
}
