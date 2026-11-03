using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Bills;
using RetailPOS.Application.Bills.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Bills;

public sealed class BillReadStore(RetailPOSDbContext dbContext)
    : IBillReadStore
{
    public async Task<BillSearchReadModel> SearchAsync(
        string? status,
        long? supplierId,
        int pageNumber,
        int pageSize,
        long businessId,
        CancellationToken cancellationToken)
    {
        var query = InBusiness(dbContext.Bills.AsNoTracking(), businessId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(bill => bill.Status == status.ToLower());
        if (supplierId.HasValue)
            query = query.Where(bill => bill.SupplierId == supplierId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var values = await query
            .OrderByDescending(bill => bill.DueDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(bill => new
            {
                bill.Id,
                bill.SupplierId,
                SupplierName = bill.Supplier.Name,
                PurchaseOrderId = bill.PoId,
                bill.AmountDue,
                bill.DueDate,
                bill.Status
            })
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var bills = values
            .Select(bill => new BillReadModel(
                bill.Id,
                bill.SupplierId,
                bill.SupplierName,
                bill.PurchaseOrderId,
                bill.AmountDue,
                bill.DueDate,
                bill.Status,
                bill.Status != "paid" && bill.DueDate < now))
            .ToList();
        return new BillSearchReadModel(bills, totalCount);
    }

    public async Task<BillReadModel?> GetByIdAsync(
        long billId,
        long businessId,
        CancellationToken cancellationToken)
    {
        var bill = await InBusiness(dbContext.Bills.AsNoTracking(), businessId)
            .Where(candidate => candidate.Id == billId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.SupplierId,
                SupplierName = candidate.Supplier.Name,
                PurchaseOrderId = candidate.PoId,
                candidate.AmountDue,
                candidate.DueDate,
                candidate.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        return bill is null
            ? null
            : new BillReadModel(
                bill.Id,
                bill.SupplierId,
                bill.SupplierName,
                bill.PurchaseOrderId,
                bill.AmountDue,
                bill.DueDate,
                bill.Status,
                bill.Status != "paid" && bill.DueDate < DateTime.UtcNow);
    }

    public async Task<BillSummaryReadModel> GetSummaryAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var unpaidBills = await InBusiness(dbContext.Bills.AsNoTracking(), businessId)
            .Where(bill => bill.Status != "paid")
            .Select(bill => new { bill.AmountDue, bill.DueDate })
            .ToListAsync(cancellationToken);
        var overdueBills = unpaidBills.Where(bill => bill.DueDate < now).ToList();
        return new BillSummaryReadModel(
            unpaidBills.Sum(bill => bill.AmountDue),
            overdueBills.Sum(bill => bill.AmountDue),
            unpaidBills.Count,
            overdueBills.Count);
    }

    private static IQueryable<RetailPOS.Core.Entities.Bill> InBusiness(
        IQueryable<RetailPOS.Core.Entities.Bill> query,
        long businessId) =>
        query.Where(bill => bill.Supplier.BusinessId == businessId);
}
