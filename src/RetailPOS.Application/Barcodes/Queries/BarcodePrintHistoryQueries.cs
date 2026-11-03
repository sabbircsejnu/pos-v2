using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.Application.Barcodes.Queries;

public sealed record SearchBarcodePrintHistoryQuery(BarcodePrintHistorySearchCriteria Criteria)
    : IQuery<BarcodePrintHistorySearchReadModel>;

public sealed class SearchBarcodePrintHistoryQueryHandler(
    IBarcodePrintHistoryReadStore store,
    IBarcodePrintHistoryReadScope readScope)
    : IRequestHandler<SearchBarcodePrintHistoryQuery, BarcodePrintHistorySearchReadModel>
{
    public async Task<BarcodePrintHistorySearchReadModel> Handle(
        SearchBarcodePrintHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var accessScope = await readScope.GetAsync(cancellationToken);
        if (!accessScope.BusinessId.HasValue)
            throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();

        var pageNumber = Math.Max(1, request.Criteria.PageNumber);
        var pageSize = Math.Clamp(request.Criteria.PageSize, 1, 100);
        return await store.SearchAsync(
            request.Criteria with { PageNumber = pageNumber, PageSize = pageSize },
            accessScope.BusinessId.Value,
            cancellationToken);
    }
}

public sealed record GetBarcodePrintHistoryByIdQuery(long Id)
    : IQuery<BarcodePrintHistoryReadModel>;

public sealed class GetBarcodePrintHistoryByIdQueryHandler(
    IBarcodePrintHistoryReadStore store,
    IBarcodePrintHistoryReadScope readScope)
    : IRequestHandler<GetBarcodePrintHistoryByIdQuery, BarcodePrintHistoryReadModel>
{
    public async Task<BarcodePrintHistoryReadModel> Handle(
        GetBarcodePrintHistoryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var accessScope = await readScope.GetAsync(cancellationToken);
        var result = await store.GetByIdAsync(request.Id, accessScope, cancellationToken);
        return result.Status switch
        {
            BarcodePrintHistoryLookupStatus.Found => result.History
                ?? throw new InvalidOperationException("Found barcode print history must include its read model."),
            BarcodePrintHistoryLookupStatus.NotFound => throw new KeyNotFoundException(
                $"Barcode print history with ID {request.Id} not found"),
            BarcodePrintHistoryLookupStatus.BusinessScopeRequired =>
                throw new RetailPOS.Application.Authorization.BusinessContextRequiredException(),
            BarcodePrintHistoryLookupStatus.Forbidden => throw new UnauthorizedAccessException(
                "You are not authorized to access this barcode print history."),
            _ => throw new InvalidOperationException("Unknown barcode print history lookup status.")
        };
    }
}
