using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Authorization;
using RetailPOS.Application.Identity.LocationAccess;

namespace RetailPOS.API.Services;

public sealed class UserOutletAccessService(
    IUserOutletAccess locationAccess,
    IRoleSwitchContext roleSwitch,
    IHttpContextAccessor httpContextAccessor) : IUserOutletAccessService
{
    public async Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync()
    {
        var result = await locationAccess.GetAuthorizedOutletsAsync(CreateContext());
        return MapAuthorizedOutlets(result);
    }

    public async Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync(LocationAccessPolicy policy)
    {
        var result = await locationAccess.GetAuthorizedLocationsAsync(CreateContext(), policy);
        return MapAuthorizedOutlets(result);
    }

    public Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        LocationAccessPolicy policy,
        long? requestedLocationId,
        string? requestedLocationType) =>
        locationAccess.ResolveAndAuthorizeLocationAsync(
            CreateContext(),
            policy,
            requestedLocationId,
            requestedLocationType);

    public Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        LocationAccessPolicy policy,
        long requestedLocationId,
        string requestedLocationType) =>
        locationAccess.EnforceWriteLocationAsync(
            CreateContext(),
            policy,
            requestedLocationId,
            requestedLocationType);

    public async Task<AuthorizedOutletsDto> GetAuthorizedOutletsForBusinessAsync(long businessId)
    {
        if (businessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(businessId));

        var context = CreateContext() with { EffectiveBusinessId = businessId };
        var result = await locationAccess.GetAuthorizedOutletsAsync(context);
        return MapAuthorizedOutlets(result);
    }

    private static AuthorizedOutletsDto MapAuthorizedOutlets(
        AuthorizedOutletsReadModel result) =>
        new()
        {
            Outlets = result.Outlets.Select(ToDto).ToList(),
            Warehouses = result.Warehouses.Select(ToDto).ToList(),
            DestinationOutlets = result.DestinationOutlets.Select(ToDto).ToList(),
            DestinationWarehouses = result.DestinationWarehouses.Select(ToDto).ToList(),
            DefaultOutletId = result.DefaultOutletId,
            IsBusinessOwner = result.IsBusinessOwner,
            IsGlobalAccess = result.IsGlobalAccess,
            AccessScope = result.AccessScope,
            DefaultLocationId = result.DefaultLocationId,
            DefaultLocationType = result.DefaultLocationType,
            Policy = result.Policy.ToString()
        };

    public Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        long? requestedLocationId,
        string? requestedLocationType) =>
        locationAccess.ResolveAndAuthorizeLocationAsync(
            CreateContext(),
            requestedLocationId,
            requestedLocationType);

    public Task<long?> ResolveAndAuthorizeOutletFilterAsync(long? requestedOutletId) =>
        locationAccess.ResolveAndAuthorizeOutletFilterAsync(CreateContext(), requestedOutletId);

    public Task<long> EnforceWriteOutletAsync(long? requestedOutletId) =>
        locationAccess.EnforceWriteOutletAsync(CreateContext(), requestedOutletId);

    public Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        long requestedLocationId,
        string requestedLocationType) =>
        locationAccess.EnforceWriteLocationAsync(
            CreateContext(),
            requestedLocationId,
            requestedLocationType);

    private UserOutletAccessContext CreateContext() =>
        new(
            roleSwitch.RealUserId,
            roleSwitch.IsSuperAdmin,
            roleSwitch.IsBusinessOwner,
            roleSwitch.EffectiveBusinessId,
            roleSwitch.EffectiveOutletId,
            HasWildcardPermission: !roleSwitch.IsRoleSwitched &&
                httpContextAccessor.HttpContext?.User.HasPermission("*") == true);

    private static AuthorizedLocationDto ToDto(AuthorizedLocationReadModel location) =>
        new() { Id = location.Id, Name = location.Name, Type = location.Type };
}
