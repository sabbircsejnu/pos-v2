using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Queries;

/// <summary>
/// POS product browser read. Authorized by the POS sale permission, so a seller never needs the
/// Product Management (products.view) permission to browse what they can sell.
/// </summary>
public sealed record GetPosCatalogueQuery(PosCatalogueCriteria Criteria)
    : IQuery<IReadOnlyList<PosCatalogueItemReadModel>>;

public sealed class GetPosCatalogueQueryHandler(IPosCatalogueReadStore store)
    : IRequestHandler<GetPosCatalogueQuery, IReadOnlyList<PosCatalogueItemReadModel>>
{
    public Task<IReadOnlyList<PosCatalogueItemReadModel>> Handle(
        GetPosCatalogueQuery request,
        CancellationToken cancellationToken) =>
        store.ListSellableVariantsAsync(request.Criteria, cancellationToken);
}

/// <summary>
/// Active categories of the caller's Business for POS filtering, without the Category Management
/// (categories.view) permission. Reuses the Business-scoped category catalog.
/// </summary>
public sealed record GetPosCategoriesQuery : IQuery<IReadOnlyList<PosCategoryReadModel>>;

public sealed class GetPosCategoriesQueryHandler(ICategoryCatalog catalog)
    : IRequestHandler<GetPosCategoriesQuery, IReadOnlyList<PosCategoryReadModel>>
{
    public async Task<IReadOnlyList<PosCategoryReadModel>> Handle(
        GetPosCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await catalog.ListAsync(cancellationToken);
        return categories
            .Where(category => category.IsActive)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new PosCategoryReadModel(
                category.Id,
                category.Name,
                category.ParentCategoryId,
                category.DisplayOrder))
            .ToArray();
    }
}
