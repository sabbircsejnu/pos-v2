using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Categories.Contracts;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Categories;

/// <summary>Categories are owned by a Business: every query is restricted to the caller's Business.</summary>
public sealed class CategoryCatalog(RetailPOSDbContext dbContext, IBusinessScope businessScope) : ICategoryCatalog
{
    private IQueryable<Category> Scoped()
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Categories.Where(category => category.BusinessId == businessId);
    }

    public async Task<IReadOnlyList<CategoryView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var businessId = businessScope.RequireBusinessId();
        var childCounts = await Scoped()
            .AsNoTracking()
            .Where(category => category.ParentCategoryId.HasValue)
            .GroupBy(category => category.ParentCategoryId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);

        var productCounts = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.BusinessId == businessId)
            .GroupBy(product => product.CategoryId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);

        var rowsById = rows.ToDictionary(row => row.Id);
        return rows.Select(row => new CategoryView(
                row.Id,
                row.Name,
                row.Description,
                row.ParentCategoryId,
                row.ParentCategoryName,
                row.ImageUrl,
                row.DisplayOrder,
                row.IsActive,
                row.CreatedAt,
                row.UpdatedAt,
                CalculateLevel(row, rowsById),
                childCounts.GetValueOrDefault(row.Id),
                productCounts.GetValueOrDefault(row.Id)))
            .ToArray();
    }

    public async Task<bool> NameExistsAsync(
        string name,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        var query = Scoped()
            .AsNoTracking()
            .Where(category => category.Name.ToLower() == name.ToLower());
        if (excludingId.HasValue)
        {
            query = query.Where(category => category.Id != excludingId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<CategoryParent?> GetParentAsync(long id, CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(cancellationToken);
        var rowsById = rows.ToDictionary(row => row.Id);
        if (!rowsById.TryGetValue(id, out var parent))
        {
            return null;
        }

        return new CategoryParent(parent.Id, parent.Name, CalculateLevel(parent, rowsById));
    }

    public Task<Category?> GetForMutationAsync(long id, CancellationToken cancellationToken) =>
        Scoped().SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<IReadOnlySet<long>> GetDescendantIdsAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var descendants = new HashSet<long>();
        var parents = new Queue<long>();
        parents.Enqueue(id);

        while (parents.TryDequeue(out var parentId))
        {
            var childIds = await Scoped()
                .AsNoTracking()
                .Where(category => category.ParentCategoryId == parentId)
                .Select(category => category.Id)
                .ToListAsync(cancellationToken);

            foreach (var childId in childIds)
            {
                if (descendants.Add(childId))
                {
                    parents.Enqueue(childId);
                }
            }
        }

        return descendants;
    }

    public Task<bool> HasChildrenAsync(long id, CancellationToken cancellationToken) =>
        Scoped()
            .AsNoTracking()
            .AnyAsync(category => category.ParentCategoryId == id, cancellationToken);

    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.CategoryId == id, cancellationToken);
    }

    public async Task<CategoryView?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var categories = await ListAsync(cancellationToken);
        return categories.SingleOrDefault(category => category.Id == id);
    }

    public async Task<CategoryView> CreateAsync(
        Category category,
        CategoryParent? parent,
        CancellationToken cancellationToken)
    {
        category.BusinessId = businessScope.RequireBusinessId();
        category.CreatedAt = DateTime.UtcNow;
        category.UpdatedAt = DateTime.UtcNow;

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CategoryView(
            category.Id,
            category.Name,
            category.Description,
            category.ParentCategoryId,
            parent?.Name,
            category.ImageUrl,
            category.DisplayOrder,
            category.IsActive,
            category.CreatedAt,
            category.UpdatedAt,
            parent is null ? 0 : parent.Level + 1,
            0,
            0);
    }

    public async Task<CategoryView> UpdateAsync(
        Category category,
        CancellationToken cancellationToken)
    {
        category.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(category.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Category with ID {category.Id} was not found after update");
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var category = await Scoped().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            return;
        }

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<CategoryRow>> LoadRowsAsync(CancellationToken cancellationToken) =>
        await Scoped()
            .AsNoTracking()
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryRow(
                category.Id,
                category.Name,
                category.Description,
                category.ParentCategoryId,
                category.ParentCategory == null ? null : category.ParentCategory.Name,
                category.ImageUrl,
                category.DisplayOrder,
                category.IsActive,
                category.CreatedAt,
                category.UpdatedAt))
            .ToListAsync(cancellationToken);

    private static int CalculateLevel(CategoryRow category, IReadOnlyDictionary<long, CategoryRow> rowsById)
    {
        var level = 0;
        var current = category;
        var visited = new HashSet<long>();

        while (current.ParentCategoryId is long parentId)
        {
            if (!visited.Add(current.Id))
            {
                throw new InvalidOperationException("Category hierarchy contains a cycle");
            }

            level++;
            if (!rowsById.TryGetValue(parentId, out current!))
            {
                break;
            }
        }

        return level;
    }

    private sealed record CategoryRow(
        long Id,
        string Name,
        string? Description,
        long? ParentCategoryId,
        string? ParentCategoryName,
        string? ImageUrl,
        int DisplayOrder,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
