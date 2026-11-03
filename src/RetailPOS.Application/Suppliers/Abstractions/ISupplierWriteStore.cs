namespace RetailPOS.Application.Suppliers.Abstractions;

public interface ISupplierWriteStore
{
    Task<bool> NameExistsAsync(
        string name,
        long businessId,
        long? excludeSupplierId,
        CancellationToken cancellationToken);

    Task<SupplierReadModel> CreateAsync(
        SupplierWriteModel supplier,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierReadModel?> UpdateAsync(
        long supplierId,
        SupplierWriteModel supplier,
        long businessId,
        CancellationToken cancellationToken);

    Task<int> GetPurchaseOrderCountAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);

    Task<int> GetBillCountAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);
}
