using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierAdvanceCreationStore(RetailPOSDbContext dbContext)
    : ISupplierAdvanceCreationStore
{
    public Task<bool> SupplierExistsAsync(
        long businessId,
        long supplierId,
        CancellationToken cancellationToken) =>
        dbContext.Suppliers.AnyAsync(
            supplier => supplier.Id == supplierId && supplier.BusinessId == businessId,
            cancellationToken);

    public Task<bool> PaymentAccountExistsAsync(
        long businessId,
        long paymentAccountId,
        CancellationToken cancellationToken) =>
        dbContext.ChartAccounts.AnyAsync(
            account => account.BusinessId == businessId && account.Id == paymentAccountId,
            cancellationToken);

    public async Task<SupplierAdvanceReadModel> CreateAsync(
        SupplierAdvance advance,
        CancellationToken cancellationToken)
    {
        dbContext.SupplierAdvances.Add(advance);
        await dbContext.SaveChangesAsync(cancellationToken);

        var persisted = await dbContext.SupplierAdvances
            .AsNoTracking()
            .Include(candidate => candidate.Supplier)
            .FirstAsync(candidate => candidate.Id == advance.Id, cancellationToken);

        return new SupplierAdvanceReadModel(
            persisted.Id,
            persisted.SupplierId,
            persisted.Supplier.Name,
            persisted.OutletId,
            persisted.PaymentDate,
            persisted.PaymentMethod,
            persisted.PaymentAccountId,
            persisted.Amount,
            persisted.RemainingAmount,
            persisted.AppliedAmount,
            persisted.Status,
            persisted.ReferenceNumber,
            persisted.Notes,
            persisted.JournalEntryId);
    }
}
