using MediatR;

namespace RetailPOS.Application.HR.Organisation;

public sealed record GetHrPositionsQuery(string? Search = null)
    : IRequest<IReadOnlyList<HrPositionReadModel>>;

public sealed class GetHrPositionsQueryHandler(
    IHrOrganisationAccess access,
    IHrPositionStore store)
    : IRequestHandler<GetHrPositionsQuery, IReadOnlyList<HrPositionReadModel>>
{
    public async Task<IReadOnlyList<HrPositionReadModel>> Handle(
        GetHrPositionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewPositions, null,
            cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        return await store.GetPositionsAsync(businessId, request.Search, scope, cancellationToken);
    }
}
