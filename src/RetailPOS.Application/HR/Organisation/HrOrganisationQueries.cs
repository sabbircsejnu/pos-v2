using MediatR;

namespace RetailPOS.Application.HR.Organisation;

public sealed record GetHrOrgUnitsQuery(string? Search = null) : IRequest<IReadOnlyList<HrOrgUnitReadModel>>;

public sealed class GetHrOrgUnitsQueryHandler(
    IHrOrganisationAccess access,
    IHrOrganisationReadStore store) : IRequestHandler<GetHrOrgUnitsQuery, IReadOnlyList<HrOrgUnitReadModel>>
{
    public async Task<IReadOnlyList<HrOrgUnitReadModel>> Handle(
        GetHrOrgUnitsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewOrgUnits, null, cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        return await store.GetOrgUnitsAsync(businessId, request.Search, scope, cancellationToken);
    }
}

public sealed record GetOperationalLocationsQuery(
    string? SourceEntityType = null,
    string? Search = null) : IRequest<IReadOnlyList<OperationalLocationReadModel>>;

public sealed class GetOperationalLocationsQueryHandler(
    IHrOrganisationAccess access,
    IHrOrganisationReadStore store) : IRequestHandler<GetOperationalLocationsQuery,
    IReadOnlyList<OperationalLocationReadModel>>
{
    public async Task<IReadOnlyList<OperationalLocationReadModel>> Handle(
        GetOperationalLocationsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewOperationalLocations, null,
            cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        return await store.GetOperationalLocationsAsync(
            businessId, request.SourceEntityType, request.Search, scope, cancellationToken);
    }
}

public sealed record GetOperationalLocationQuery(long ProjectionId)
    : IRequest<OperationalLocationReadModel?>;

public sealed class GetOperationalLocationQueryHandler(
    IHrOrganisationAccess access,
    IHrOrganisationReadStore store)
    : IRequestHandler<GetOperationalLocationQuery, OperationalLocationReadModel?>
{
    public async Task<OperationalLocationReadModel?> Handle(
        GetOperationalLocationQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ProjectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ProjectionId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewOperationalLocations,
            request.ProjectionId, cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        return await store.GetOperationalLocationAsync(
            businessId, request.ProjectionId, scope, cancellationToken);
    }
}
