using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Grns.Queries;

public sealed record SearchGrnsQuery(
    long? PurchaseOrderId,
    string? Status,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize,
    string? LocationType = null,
    long? LocationId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null) : IQuery<GrnSearchReadModel>;

public sealed class SearchGrnsQueryHandler(
    IGrnReadStore store,
    IGrnReadScopeProvider scopeProvider,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<SearchGrnsQuery, GrnSearchReadModel>
{
    public async Task<GrnSearchReadModel> Handle(
        SearchGrnsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FromDate > request.ToDate)
            throw new InvalidOperationException("From Date cannot be later than To Date.");

        var locationType = string.IsNullOrWhiteSpace(request.LocationType)
            ? null
            : PurchaseLocationTypes.Normalize(request.LocationType);
        if (request.LocationId.HasValue && locationType is null)
            throw new InvalidOperationException("Location Type is required when a Location is selected.");

        var scope = await scopeProvider.GetAsync(cancellationToken);
        if (request.LocationId is long locationId && !IsAllowed(scope, locationType!, locationId))
            throw new UnauthorizedAccessException($"You are not authorized to view GRNs for {locationType} #{locationId}.");

        return await store.SearchAsync(
            new GrnSearchCriteria(
                request.PurchaseOrderId,
                request.Status,
                request.StartDate,
                request.EndDate,
                locationType,
                request.LocationId,
                await BuildReceivedWindowAsync(request, scope.BusinessId, cancellationToken),
                request.PageNumber,
                request.PageSize),
            scope,
            cancellationToken);
    }

    private static bool IsAllowed(GrnReadScope scope, string locationType, long locationId) =>
        locationType == PurchaseLocationTypes.Outlet
            ? scope.AllowsOutlet(locationId)
            : scope.AllowsWarehouse(locationId);

    private async Task<GrnReceivedWindow?> BuildReceivedWindowAsync(
        SearchGrnsQuery request,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!request.FromDate.HasValue && !request.ToDate.HasValue)
            return null;

        var (fromUtc, toExclusiveUtc) = CompanyDates.DayWindow(
            request.FromDate,
            request.ToDate,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        return new GrnReceivedWindow(fromUtc, toExclusiveUtc);
    }
}
