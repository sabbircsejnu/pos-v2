using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(long id);
    Task<IEnumerable<Supplier>> GetAllAsync();
    Task<(IEnumerable<Supplier> Suppliers, int TotalCount)> SearchAsync(
        string? searchQuery,
        decimal? minCreditLimit,
        decimal? maxCreditLimit,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder);
    Task<Supplier> CreateAsync(Supplier supplier);
    Task<Supplier> UpdateAsync(Supplier supplier);
    Task<bool> DeleteAsync(long id);
    Task<bool> NameExistsAsync(string name, long? excludeId = null);
    Task<int> GetTotalPurchaseOrdersAsync(long supplierId);
    Task<int> GetTotalBillsAsync(long supplierId);
    Task<decimal> GetTotalPurchaseAmountAsync(long supplierId);
    Task<decimal> GetOutstandingBalanceAsync(long supplierId);
}
