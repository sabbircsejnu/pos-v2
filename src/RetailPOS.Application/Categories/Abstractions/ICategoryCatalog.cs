using RetailPOS.Application.Categories.Contracts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Categories.Abstractions;

public interface ICategoryCatalog
{
    Task<IReadOnlyList<CategoryView>> ListAsync(CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        string name,
        long? excludingId,
        CancellationToken cancellationToken);

    Task<CategoryParent?> GetParentAsync(long id, CancellationToken cancellationToken);

    Task<Category?> GetForMutationAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlySet<long>> GetDescendantIdsAsync(long id, CancellationToken cancellationToken);

    Task<bool> HasChildrenAsync(long id, CancellationToken cancellationToken);

    Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken);

    Task<CategoryView?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<CategoryView> CreateAsync(
        Category category,
        CategoryParent? parent,
        CancellationToken cancellationToken);

    Task<CategoryView> UpdateAsync(Category category, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
