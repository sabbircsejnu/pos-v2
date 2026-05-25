using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

/// <summary>
/// Authorization rule:
/// - BusinessOwner ⇒ every outlet + every warehouse.
/// - Everyone else ⇒ only the outlet recorded on their User row (or the
///   currently-acting outlet, if role-switched).
/// </summary>
public class UserOutletAccessService : IUserOutletAccessService
{
    private readonly RetailPOSDbContext _context;
    private readonly IRoleSwitchContext _roleSwitch;

    public UserOutletAccessService(RetailPOSDbContext context, IRoleSwitchContext roleSwitch)
    {
        _context = context;
        _roleSwitch = roleSwitch;
    }

    public async Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync()
    {
        var userId = _roleSwitch.RealUserId
            ?? throw new UnauthorizedAccessException("User identity not found");

        var defaultOutletId = _roleSwitch.EffectiveOutletId;

        if (_roleSwitch.IsBusinessOwner)
        {
            var allOutlets = await _context.Outlets.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new AuthorizedLocationDto { Id = o.Id, Name = o.Name, Type = "outlet" })
                .ToListAsync();

            var allWarehouses = await _context.Warehouses.AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new AuthorizedLocationDto { Id = w.Id, Name = w.Name, Type = "warehouse" })
                .ToListAsync();

            return new AuthorizedOutletsDto
            {
                Outlets = allOutlets,
                Warehouses = allWarehouses,
                DefaultOutletId = defaultOutletId,
                IsBusinessOwner = true
            };
        }

        // Non-owner: scoped to the user's home outlet (or acting outlet).
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new UnauthorizedAccessException("User not found");

        var allowedOutletId = defaultOutletId ?? user.OutletId;

        var outlets = new List<AuthorizedLocationDto>();
        if (allowedOutletId.HasValue)
        {
            var outlet = await _context.Outlets.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == allowedOutletId.Value);
            if (outlet != null)
                outlets.Add(new AuthorizedLocationDto { Id = outlet.Id, Name = outlet.Name, Type = "outlet" });
        }

        return new AuthorizedOutletsDto
        {
            Outlets = outlets,
            Warehouses = new List<AuthorizedLocationDto>(),
            DefaultOutletId = allowedOutletId,
            IsBusinessOwner = false
        };
    }

    public async Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        long? requestedLocationId,
        string? requestedLocationType)
    {
        var auth = await GetAuthorizedOutletsAsync();

        // No filter requested → fall back to the user's default outlet (or "all" for BO).
        if (!requestedLocationId.HasValue)
        {
            if (auth.IsBusinessOwner)
                return (null, null);

            return auth.DefaultOutletId.HasValue
                ? (auth.DefaultOutletId, "outlet")
                : (null, null);
        }

        var type = string.IsNullOrWhiteSpace(requestedLocationType) ? "outlet" : requestedLocationType.ToLower();

        var allowed = type == "warehouse"
            ? auth.Warehouses.Any(w => w.Id == requestedLocationId.Value)
            : auth.Outlets.Any(o => o.Id == requestedLocationId.Value);

        if (!allowed)
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for {type} #{requestedLocationId.Value}.");

        return (requestedLocationId, type);
    }

    public async Task<long?> ResolveAndAuthorizeOutletFilterAsync(long? requestedOutletId)
    {
        var auth = await GetAuthorizedOutletsAsync();

        if (!auth.IsBusinessOwner)
        {
            // Non-BO is always pinned to their default outlet, regardless of what
            // the client sent — frontend filters cannot expand scope.
            return auth.DefaultOutletId
                ?? throw new UnauthorizedAccessException(
                    "Your account is not assigned to a default outlet. Contact an administrator.");
        }

        if (!requestedOutletId.HasValue)
            return null; // BO: "all outlets"

        var ok = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
        if (!ok)
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for outlet #{requestedOutletId.Value}.");

        return requestedOutletId;
    }

    public async Task<long> EnforceWriteOutletAsync(long? requestedOutletId)
    {
        var auth = await GetAuthorizedOutletsAsync();

        if (!auth.IsBusinessOwner)
        {
            var defaultOutlet = auth.DefaultOutletId
                ?? throw new UnauthorizedAccessException(
                    "Your account is not assigned to a default outlet. Contact an administrator.");

            // Reject mismatched payloads instead of silently rewriting — protects audit trail.
            if (requestedOutletId.HasValue && requestedOutletId.Value != defaultOutlet)
                throw new UnauthorizedAccessException(
                    $"You are not authorized to record this transaction for outlet #{requestedOutletId.Value}.");

            return defaultOutlet;
        }

        if (!requestedOutletId.HasValue || requestedOutletId.Value <= 0)
            throw new InvalidOperationException("OutletId is required for this transaction.");

        var ok = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
        if (!ok)
            throw new UnauthorizedAccessException(
                $"Outlet #{requestedOutletId.Value} is not in your authorized set.");

        return requestedOutletId.Value;
    }

    public async Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        long requestedLocationId,
        string requestedLocationType)
    {
        if (requestedLocationId <= 0)
            throw new InvalidOperationException("locationId is required.");

        var type = string.IsNullOrWhiteSpace(requestedLocationType)
            ? "outlet"
            : requestedLocationType.ToLower();

        if (type != "outlet" && type != "warehouse")
            throw new InvalidOperationException($"Invalid locationType '{requestedLocationType}'.");

        var auth = await GetAuthorizedOutletsAsync();

        if (!auth.IsBusinessOwner)
        {
            var defaultOutlet = auth.DefaultOutletId
                ?? throw new UnauthorizedAccessException(
                    "Your account is not assigned to a default outlet. Contact an administrator.");

            if (type != "outlet" || requestedLocationId != defaultOutlet)
                throw new UnauthorizedAccessException(
                    $"You are not authorized to record this transaction for {type} #{requestedLocationId}.");

            return (defaultOutlet, "outlet");
        }

        var allowed = type == "warehouse"
            ? auth.Warehouses.Any(w => w.Id == requestedLocationId)
            : auth.Outlets.Any(o => o.Id == requestedLocationId);

        if (!allowed)
            throw new UnauthorizedAccessException(
                $"{type} #{requestedLocationId} is not in your authorized set.");

        return (requestedLocationId, type);
    }
}
