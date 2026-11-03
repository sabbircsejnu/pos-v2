using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPaymentReversalStore(
    RetailPOSDbContext dbContext,
    ISupplierPaymentReversalJournalPoster journalPoster,
    ISupplierPaymentReversalAuditWriter auditWriter)
    : ISupplierPaymentReversalStore
{
    public Task<SupplierPaymentReversalScope?> FindForReversalAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierPayments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId && payment.BusinessId == businessId)
            .Select(payment => new SupplierPaymentReversalScope(payment.OutletId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SupplierPaymentReadModel> ReverseAsync(
        long paymentId,
        long businessId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplierPayments
            .FirstOrDefaultAsync(
                candidate => candidate.Id == paymentId && candidate.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier payment {paymentId} was not found.");

        if (payment.Status == "reversed")
            return await FindByIdAsync(payment.Id, cancellationToken);
        if (payment.Status != "posted" || !payment.JournalEntryId.HasValue)
            throw new InvalidOperationException("Only posted supplier payments can be reversed.");

        var period = await dbContext.AccountingPeriods
            .Include(candidate => candidate.FinancialYear)
            .FirstOrDefaultAsync(
                candidate => candidate.FinancialYear.BusinessId == businessId
                    && candidate.StartDate <= postingDate
                    && candidate.EndDate >= postingDate,
                cancellationToken);
        if (period is null
            || !string.Equals(
                period.Status,
                AccountingPeriodStatuses.Open,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The accounting period is closed for this date.");

        var relatedAdvances = await dbContext.SupplierAdvances
            .Where(advance => advance.BusinessId == businessId
                && advance.SourcePaymentId == payment.Id
                && advance.Status == "posted")
            .ToListAsync(cancellationToken);
        foreach (var advance in relatedAdvances)
        {
            if (await dbContext.SupplierAdvanceUtilizations.AnyAsync(
                    utilization => utilization.SupplierAdvanceId == advance.Id
                        && utilization.Status == "effective",
                    cancellationToken))
                throw new InvalidOperationException(
                    "Reverse dependent supplier advance utilizations before reversing this payment.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var reversalJournalId = await journalPoster.ReverseJournalAsync(
                businessId,
                payment.JournalEntryId.Value,
                postingDate,
                reason,
                actorUserId,
                cancellationToken);
            var allocations = await dbContext.SupplierPaymentAllocations
                .Where(allocation => allocation.SupplierPaymentId == payment.Id
                    && allocation.Status == "effective"
                    && !allocation.IsReversed)
                .ToListAsync(cancellationToken);
            foreach (var allocation in allocations)
            {
                allocation.IsReversed = true;
                allocation.Status = "reversed";
                allocation.ReversedEffectiveDate = postingDate;
                allocation.ReversalJournalEntryId = reversalJournalId;
                allocation.ReversalOfAllocationId = allocation.Id;
            }

            var affectedPayableIds = allocations
                .Where(allocation => allocation.SupplierPayableDocumentId.HasValue)
                .Select(allocation => allocation.SupplierPayableDocumentId!.Value)
                .Distinct()
                .ToList();
            foreach (var payableId in affectedPayableIds)
            {
                var payable = await dbContext.SupplierPayableDocuments
                    .SingleAsync(document => document.Id == payableId, cancellationToken);
                var remainingPaymentAllocations = await dbContext.SupplierPaymentAllocations
                    .Where(allocation => allocation.SupplierPayableDocumentId == payableId
                        && allocation.SupplierPaymentId != payment.Id
                        && allocation.Status == "effective"
                        && !allocation.IsReversed)
                    .SumAsync(allocation => allocation.AllocatedAmount, cancellationToken);
                var remainingAdvanceUtilizations = await dbContext.SupplierAdvanceUtilizations
                    .Where(utilization => utilization.SupplierPayableDocumentId == payableId
                        && utilization.Status == "effective")
                    .SumAsync(utilization => utilization.Amount, cancellationToken);
                var outstanding = payable.OriginalPayableAmount
                    - remainingPaymentAllocations
                    - remainingAdvanceUtilizations;
                payable.Status = outstanding <= 0m
                    ? SupplierPayableDocumentStatuses.Paid
                    : outstanding < payable.OriginalPayableAmount
                        ? SupplierPayableDocumentStatuses.Partial
                        : SupplierPayableDocumentStatuses.Open;
            }

            foreach (var advance in relatedAdvances)
            {
                if (advance.JournalEntryId.HasValue)
                    await journalPoster.ReverseJournalAsync(
                        businessId,
                        advance.JournalEntryId.Value,
                        postingDate,
                        reason,
                        actorUserId,
                        cancellationToken);
                advance.Status = "reversed";
                advance.RemainingAmount = 0;
                advance.AppliedAmount = 0;
            }

            payment.Status = "reversed";
            payment.ReversalJournalEntryId = reversalJournalId;
            payment.ReversedAt = postingDate;
            payment.ReversedBy = actorUserId;
            payment.ReversalReason = reason;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordReversedAsync(
                new SupplierPaymentReversalAuditRecord(
                    payment.Id,
                    payment.BusinessId,
                    payment.OutletId,
                    payment.SupplierId,
                    payment.Amount,
                    payment.PaymentMethod,
                    payment.PaymentAccountId,
                    payment.JournalEntryId,
                    payment.ReversalJournalEntryId,
                    payment.ReversalReason,
                    payment.ReversedAt.Value,
                    BuildAllocationSummary(allocations),
                    actorUserId),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await FindByIdAsync(payment.Id, cancellationToken);
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

    private static string BuildAllocationSummary(
        IEnumerable<SupplierPaymentAllocation> allocations) =>
        string.Join(
            ";",
            allocations.Select(allocation =>
                $"{allocation.PayableDocumentType}:{allocation.PayableDocumentId}:{allocation.AllocatedAmount:0.00}:{allocation.Status}"));
}
