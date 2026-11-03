using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Suppliers;
using RetailPOS.Application.Suppliers.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Suppliers;

public sealed class SupplierWriteStore(RetailPOSDbContext dbContext)
    : ISupplierWriteStore
{
    public Task<bool> NameExistsAsync(
        string name,
        long businessId,
        long? excludeSupplierId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Suppliers
            .Where(supplier =>
                supplier.BusinessId == businessId
                && supplier.Name.ToLower() == name.ToLower());
        if (excludeSupplierId.HasValue)
            query = query.Where(supplier => supplier.Id != excludeSupplierId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<SupplierReadModel> CreateAsync(
        SupplierWriteModel input,
        long businessId,
        CancellationToken cancellationToken)
    {
        var supplier = new Supplier
        {
            BusinessId = businessId,
            Name = input.Name,
            Contact = input.Contact,
            Address = input.Address,
            CreditLimit = input.CreditLimit
        };
        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(supplier);
    }

    public async Task<SupplierReadModel?> UpdateAsync(
        long supplierId,
        SupplierWriteModel input,
        long businessId,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .FirstOrDefaultAsync(
                candidate => candidate.Id == supplierId && candidate.BusinessId == businessId,
                cancellationToken);
        if (supplier is null)
            return null;

        supplier.Name = input.Name;
        supplier.Contact = input.Contact;
        supplier.Address = input.Address;
        supplier.CreditLimit = input.CreditLimit;
        supplier.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(supplier);
    }

    public Task<int> GetPurchaseOrderCountAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders
            .Where(order => order.SupplierId == supplierId)
            .WhereBusiness(businessId)
            .CountAsync(cancellationToken);

    public Task<int> GetBillCountAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.Bills
            .Where(bill =>
                bill.SupplierId == supplierId
                && bill.Supplier.BusinessId == businessId)
            .CountAsync(cancellationToken);

    public async Task<bool> DeleteAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .FirstOrDefaultAsync(
                candidate => candidate.Id == supplierId && candidate.BusinessId == businessId,
                cancellationToken);
        if (supplier is null)
            return false;

        dbContext.Suppliers.Remove(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static SupplierReadModel ToReadModel(Supplier supplier) =>
        new(
            supplier.Id,
            supplier.Name,
            supplier.Contact,
            supplier.Address,
            supplier.CreditLimit,
            supplier.CreatedAt,
            supplier.UpdatedAt);
}
