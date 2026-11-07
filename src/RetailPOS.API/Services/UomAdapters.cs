using System.Security.Claims;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Services;

public sealed class UomOperationScopeAdapter(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService locationAccess,
    IHttpContextAccessor httpContextAccessor) : IUomOperationScope
{
    public long RequireBusinessId() => tenantAccess.RequireBusinessId();

    public long RequireActorUserId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("The current user could not be identified.");
    }

    public async Task AuthorizeWriteLocationAsync(long locationId, string locationType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await locationAccess.EnforceWriteLocationAsync(LocationAccessPolicy.InventoryStockView, locationId, locationType);
    }

    public async Task<(IReadOnlyList<long>? OutletIds, IReadOnlyList<long>? WarehouseIds)> GetReadableLocationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = await locationAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.InventoryStockView);
        return authorized.IsGlobalAccess
            ? (null, null)
            : (authorized.Outlets.Select(location => location.Id).ToList(), authorized.Warehouses.Select(location => location.Id).ToList());
    }

    public async Task AuthorizeReadLocationAsync(long locationId, string locationType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await locationAccess.ResolveAndAuthorizeLocationAsync(LocationAccessPolicy.InventoryStockView, locationId, locationType);
    }
}

public sealed class UomAuditWriter(IAuditService audit) : IUomAuditWriter
{
    public Task RecordAsync(
        string summary,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Adjustment,
            Module = AuditModule.Inventory,
            Summary = summary,
            PrimaryEntity = (entityType, entityId),
            Metadata = metadata.ToDictionary(pair => pair.Key, pair => pair.Value)
        },
            cancellationToken);
}
