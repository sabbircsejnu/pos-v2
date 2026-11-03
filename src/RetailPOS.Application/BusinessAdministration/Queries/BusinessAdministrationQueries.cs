using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.Application.BusinessAdministration.Queries;

public sealed record GetBusinessesQuery(string? Search, bool? IsActive)
    : IQuery<IReadOnlyList<BusinessSummaryReadModel>>;

public sealed class GetBusinessesQueryHandler(IBusinessAdministrationReadStore store)
    : IRequestHandler<GetBusinessesQuery, IReadOnlyList<BusinessSummaryReadModel>>
{
    public Task<IReadOnlyList<BusinessSummaryReadModel>> Handle(
        GetBusinessesQuery request,
        CancellationToken cancellationToken) =>
        store.ListBusinessesAsync(request.Search, request.IsActive, cancellationToken);
}

public sealed record GetBusinessByIdQuery(long BusinessId) : IQuery<BusinessSummaryReadModel>;

public sealed class GetBusinessByIdQueryHandler(IBusinessAdministrationReadStore store)
    : IRequestHandler<GetBusinessByIdQuery, BusinessSummaryReadModel>
{
    public async Task<BusinessSummaryReadModel> Handle(
        GetBusinessByIdQuery request,
        CancellationToken cancellationToken) =>
        await store.FindBusinessAsync(request.BusinessId, cancellationToken)
        ?? throw new KeyNotFoundException("Business not found.");
}

public sealed record GetBusinessFeatureSettingsQuery(long BusinessId)
    : IQuery<IReadOnlyList<BusinessFeatureSettingReadModel>>;

public sealed class GetBusinessFeatureSettingsQueryHandler(IBusinessAdministrationReadStore store)
    : IRequestHandler<GetBusinessFeatureSettingsQuery, IReadOnlyList<BusinessFeatureSettingReadModel>>
{
    public async Task<IReadOnlyList<BusinessFeatureSettingReadModel>> Handle(
        GetBusinessFeatureSettingsQuery request,
        CancellationToken cancellationToken) =>
        await store.ListFeatureSettingsAsync(request.BusinessId, cancellationToken)
        ?? throw new KeyNotFoundException("Business not found.");
}
