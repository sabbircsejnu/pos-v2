using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPaymentCreationStore(RetailPOSDbContext dbContext)
    : ISupplierPaymentCreationStore, ISupplierPaymentSubmissionStore
{
    public Task<bool> SupplierExistsAsync(
        long businessId,
        long supplierId,
        CancellationToken cancellationToken) =>
        dbContext.Suppliers
            .AnyAsync(supplier =>
                supplier.Id == supplierId && supplier.BusinessId == businessId,
                cancellationToken);

    public Task<bool> PaymentAccountExistsAsync(
        long businessId,
        long paymentAccountId,
        CancellationToken cancellationToken) =>
        dbContext.ChartAccounts
            .AnyAsync(account =>
                account.BusinessId == businessId && account.Id == paymentAccountId,
                cancellationToken);

    public async Task CreateDraftAsync(
        SupplierPayment payment,
        CancellationToken cancellationToken)
    {
        dbContext.SupplierPayments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<SupplierPayableDocument?> FindPayableDocumentAsync(
        long businessId,
        long supplierId,
        long payableDocumentId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierPayableDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(document =>
                document.Id == payableDocumentId &&
                document.BusinessId == businessId &&
                document.SupplierId == supplierId,
                cancellationToken);

    public Task<Bill?> FindBillAsync(
        long businessId,
        long supplierId,
        long billId,
        CancellationToken cancellationToken) =>
        dbContext.Bills
            .AsNoTracking()
            .Where(bill =>
                bill.Id == billId &&
                bill.SupplierId == supplierId &&
                bill.Supplier.BusinessId == businessId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<decimal> GetPayableOutstandingAsync(
        long payableDocumentId,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.SupplierPayableDocuments
            .AsNoTracking()
            .SingleAsync(document => document.Id == payableDocumentId, cancellationToken);
        var paymentAllocations = await dbContext.SupplierPaymentAllocations
            .Where(allocation =>
                allocation.SupplierPayableDocumentId == payableDocumentId &&
                allocation.Status == "effective" &&
                !allocation.IsReversed)
            .SumAsync(allocation => allocation.AllocatedAmount, cancellationToken);
        var utilizations = await dbContext.SupplierAdvanceUtilizations
            .Where(utilization =>
                utilization.SupplierPayableDocumentId == payableDocumentId &&
                utilization.Status == "effective")
            .SumAsync(utilization => utilization.Amount, cancellationToken);
        return document.OriginalPayableAmount - paymentAllocations - utilizations;
    }

    public async Task CreateAllocationsAsync(
        IReadOnlyList<SupplierPaymentAllocation> allocations,
        CancellationToken cancellationToken)
    {
        dbContext.SupplierPaymentAllocations.AddRange(allocations);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierPaymentReadModel> FindByIdAsync(
        long paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplierPayments
            .AsNoTracking()
            .Include(candidate => candidate.Supplier)
            .FirstAsync(candidate => candidate.Id == paymentId, cancellationToken);

        return new SupplierPaymentReadModel(
            payment.Id,
            payment.SupplierId,
            payment.Supplier.Name,
            payment.OutletId,
            payment.PaymentDate,
            payment.PostingDate,
            payment.PaymentMethod,
            payment.PaymentAccountId,
            payment.Amount,
            payment.AllocatedAmount,
            payment.AdvanceAmount,
            payment.Status,
            payment.ReferenceNumber,
            payment.Notes,
            payment.JournalEntryId);
    }

    public async Task<SupplierPaymentSubmissionSnapshot?> FindForSubmissionAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplierPayments
            .AsNoTracking()
            .Where(candidate => candidate.Id == paymentId && candidate.BusinessId == businessId)
            .Select(candidate => new SupplierPaymentSubmissionSnapshot(
                candidate.Id,
                candidate.OutletId,
                candidate.Status,
                candidate.SupplierId,
                candidate.Amount,
                candidate.PaymentMethod,
                candidate.PaymentAccountId,
                candidate.JournalEntryId,
                candidate.ReversalJournalEntryId))
            .FirstOrDefaultAsync(cancellationToken);
        return payment;
    }

    public async Task SubmitAsync(
        long paymentId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplierPayments
            .FirstOrDefaultAsync(
                candidate => candidate.Id == paymentId && candidate.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier payment {paymentId} was not found.");

        payment.Status = "submitted";
        payment.SubmittedBy = actorUserId;
        payment.SubmittedAt = DateTime.UtcNow;
        payment.UpdatedBy = actorUserId;
        payment.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
