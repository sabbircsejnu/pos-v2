using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Categories.Contracts;

namespace RetailPOS.Application.Categories.Queries;

public sealed record GetCategoriesQuery : IQuery<IReadOnlyList<CategoryView>>;

public sealed class GetCategoriesQueryHandler(ICategoryCatalog catalog)
    : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryView>>
{
    public Task<IReadOnlyList<CategoryView>> Handle(
        GetCategoriesQuery request,
        CancellationToken cancellationToken) =>
        catalog.ListAsync(cancellationToken);
}

public sealed record GetCategoryByIdQuery(long Id)
    : IQuery<CategoryView?>;

public sealed class GetCategoryByIdQueryHandler(ICategoryCatalog catalog)
    : IRequestHandler<GetCategoryByIdQuery, CategoryView?>
{
    public async Task<CategoryView?> Handle(
        GetCategoryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await catalog.ListAsync(cancellationToken);
        return categories.SingleOrDefault(category => category.Id == request.Id);
    }
}

public sealed record GetCategoryChildrenQuery(long ParentCategoryId)
    : IQuery<IReadOnlyList<CategoryView>>;

public sealed class GetCategoryChildrenQueryHandler(ICategoryCatalog catalog)
    : IRequestHandler<GetCategoryChildrenQuery, IReadOnlyList<CategoryView>>
{
    public async Task<IReadOnlyList<CategoryView>> Handle(
        GetCategoryChildrenQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await catalog.ListAsync(cancellationToken);
        return categories
            .Where(category => category.ParentCategoryId == request.ParentCategoryId)
            .ToArray();
    }
}

public sealed record CategoryTreeReadModel(
    long Id,
    string Name,
    string? Description,
    long? ParentCategoryId,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive,
    int Level,
    int ProductCount,
    IReadOnlyList<CategoryTreeReadModel> Children);

public sealed record GetCategoryTreeQuery
    : IQuery<IReadOnlyList<CategoryTreeReadModel>>;

public sealed class GetCategoryTreeQueryHandler(ICategoryCatalog catalog)
    : IRequestHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryTreeReadModel>>
{
    public async Task<IReadOnlyList<CategoryTreeReadModel>> Handle(
        GetCategoryTreeQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await catalog.ListAsync(cancellationToken);
        var childrenByParent = categories
            .Where(category => category.ParentCategoryId.HasValue)
            .GroupBy(category => category.ParentCategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return categories
            .Where(category => category.ParentCategoryId is null)
            .Select(category => BuildTree(category, 0))
            .ToArray();

        CategoryTreeReadModel BuildTree(CategoryView category, int level)
        {
            var children = childrenByParent.TryGetValue(category.Id, out var descendants)
                ? descendants.Select(child => BuildTree(child, level + 1)).ToArray()
                : [];

            return new CategoryTreeReadModel(
                category.Id,
                category.Name,
                category.Description,
                category.ParentCategoryId,
                category.ImageUrl,
                category.DisplayOrder,
                category.IsActive,
                level,
                category.ProductCount,
                children);
        }
    }
}
