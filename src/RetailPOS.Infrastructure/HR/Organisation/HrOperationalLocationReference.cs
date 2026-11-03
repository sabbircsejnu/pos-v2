using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Organisation;

internal static class HrOperationalLocationReference
{
    public static async Task EnsureAuthorizedAsync(RetailPOSDbContext db, long business, long id,
        HrOperationalLocationReadScope scope, CancellationToken ct)
    {
        if (scope.BusinessId != business) throw new UnauthorizedAccessException("Location scope is outside Business.");
        if (scope.AllowedSources is null) return;
        var l = await db.OperationalLocationProjections.AsNoTracking().SingleOrDefaultAsync(p => p.BusinessId == business && p.Id == id, ct);
        if (l is null || !scope.AllowedSources.Contains(new OperationalLocationSourceIdentity(l.SourceEntityType, l.SourceEntityId)))
            throw new UnauthorizedAccessException("Operational location changed or is outside the caller's identity scope.");
    }
    public static async Task<OperationalLocationProjection> RequireOwnedAsync(
        RetailPOSDbContext db, long business, long id, CancellationToken ct)
    {
        var location = await db.OperationalLocationProjections.AsNoTracking()
            .SingleOrDefaultAsync(l => l.BusinessId == business && l.Id == id, ct);
        if (location is null || !location.IsSourceActive || location.IsSourceDeleted
            || location.SyncStatus != "Current" || location.SourceSystem != "RetailPOS"
            || !long.TryParse(location.SourceEntityId, out var sourceId))
            throw new ArgumentException("An active, current, same-Business HR operational location is required.");
        var owned = location.SourceEntityType switch
        {
            "Outlet" => await db.Outlets.AnyAsync(o => o.BusinessId == business && o.Id == sourceId, ct),
            "Warehouse" => await db.Warehouses.AnyAsync(w => w.BusinessId == business && w.Id == sourceId, ct),
            _ => false
        };
        if (!owned) throw new ArgumentException("The operational location source is not owned by this Business.");
        return location;
    }
}
