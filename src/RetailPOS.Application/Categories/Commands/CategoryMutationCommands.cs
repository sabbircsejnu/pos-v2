using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Categories.Contracts;

namespace RetailPOS.Application.Categories.Commands;

public sealed record UpdateCategoryCommand(
    long Id,
    string Name,
    string? Description,
    long? ParentCategoryId,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive) : ICommand<CategoryView>;

public sealed class UpdateCategoryCommandHandler(ICategoryCatalog catalog)
    : IRequestHandler<UpdateCategoryCommand, CategoryView>
{
    public async Task<CategoryView> Handle(
        UpdateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var category = await catalog.GetForMutationAsync(request.Id, cancellationToken);
        if (category is null)
        {
            throw new KeyNotFoundException($"Category with ID {request.Id} not found");
        }

        if (await catalog.NameExistsAsync(request.Name, request.Id, cancellationToken))
        {
            throw new InvalidOperationException($"Category with name '{request.Name}' already exists");
        }

        if (request.ParentCategoryId is long parentId)
        {
            if (parentId == request.Id)
            {
                throw new InvalidOperationException("Category cannot be its own parent");
            }

            var descendants = await catalog.GetDescendantIdsAsync(request.Id, cancellationToken);
            if (descendants.Contains(parentId))
            {
                throw new InvalidOperationException("Cannot set a descendant category as parent (circular reference)");
            }

            if (await catalog.GetParentAsync(parentId, cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Parent category with ID {parentId} not found");
            }
        }

        category.Name = request.Name;
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.ImageUrl = request.ImageUrl;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;

        return await catalog.UpdateAsync(category, cancellationToken);
    }
}

public sealed record MoveCategoryCommand(
    long Id,
    long? NewParentCategoryId,
    int? NewDisplayOrder) : ICommand<CategoryView>;

public sealed class MoveCategoryCommandHandler(ICategoryCatalog catalog)
    : IRequestHandler<MoveCategoryCommand, CategoryView>
{
    public async Task<CategoryView> Handle(
        MoveCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var category = await catalog.GetForMutationAsync(request.Id, cancellationToken);
        if (category is null)
        {
            throw new KeyNotFoundException($"Category with ID {request.Id} not found");
        }

        if (request.NewParentCategoryId is long parentId)
        {
            if (parentId == request.Id)
            {
                throw new InvalidOperationException("Category cannot be its own parent");
            }

            var descendants = await catalog.GetDescendantIdsAsync(request.Id, cancellationToken);
            if (descendants.Contains(parentId))
            {
                throw new InvalidOperationException("Cannot move category to one of its descendants (circular reference)");
            }

            if (await catalog.GetParentAsync(parentId, cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Parent category with ID {parentId} not found");
            }
        }

        category.ParentCategoryId = request.NewParentCategoryId;
        if (request.NewDisplayOrder.HasValue)
        {
            category.DisplayOrder = request.NewDisplayOrder.Value;
        }

        return await catalog.UpdateAsync(category, cancellationToken);
    }
}

public sealed record DeleteCategoryCommand(long Id) : ICommand;

public sealed class DeleteCategoryCommandHandler(ICategoryCatalog catalog)
    : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(
        DeleteCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (await catalog.GetForMutationAsync(request.Id, cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Category with ID {request.Id} not found");
        }

        if (await catalog.HasChildrenAsync(request.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot delete category that has child categories. Delete or move children first.");
        }

        if (await catalog.HasProductsAsync(request.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot delete category that has products. Reassign or delete products first.");
        }

        await catalog.DeleteAsync(request.Id, cancellationToken);
    }
}
