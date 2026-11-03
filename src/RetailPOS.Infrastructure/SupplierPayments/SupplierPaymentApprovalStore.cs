using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPaymentApprovalStore(
    RetailPOSDbContext dbContext,
    ISupplierPaymentApprovalJournalPoster journalPoster,
    ISupplierPaymentApprovalAuditWriter auditWriter)
    : ISupplierPaymentApprovalStore
{
    public Task<SupplierPaymentApprovalScope?> FindForApprovalAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierPayments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId && payment.BusinessId == businessId)
            .Select(payment => new SupplierPaymentApprovalScope(
                payment.OutletId,
                payment.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SupplierPaymentReadModel> ApproveAsync(
        long paymentId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (string.Equals(
                    dbContext.Database.ProviderName,
                    "Npgsql.EntityFrameworkCore.PostgreSQL",
                    StringComparison.Ordinal))
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM supplier_payments WHERE id = {paymentId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
            }

            var payment = await dbContext.SupplierPayments
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == paymentId && candidate.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Supplier payment {paymentId} was not found.");

            if (!string.Equals(payment.Status, "submitted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only submitted supplier payments can be approved.");

            var paymentAccountExists = await dbContext.ChartAccounts.AnyAsync(
                account => account.BusinessId == businessId && account.Id == payment.PaymentAccountId,
                cancellationToken);
            if (!paymentAccountExists)
                throw new InvalidOperationException("Payment account was not found.");

            var postingAccounts = await journalPoster.ResolvePostingAccountsAsync(cancellationToken);
            var period = await dbContext.AccountingPeriods
                .Include(candidate => candidate.FinancialYear)
                .FirstOrDefaultAsync(
                    candidate => candidate.FinancialYear.BusinessId == businessId
                        && candidate.StartDate <= payment.PaymentDate.Date
                        && candidate.EndDate >= payment.PaymentDate.Date,
                    cancellationToken);
            if (period is null
                || !string.Equals(
                    period.Status,
                    AccountingPeriodStatuses.Open,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The accounting period is closed for this date.");

            var allocationTotal = await dbContext.SupplierPaymentAllocations
                .Where(allocation => allocation.SupplierPaymentId == payment.Id)
                .SumAsync(allocation => allocation.AllocatedAmount, cancellationToken);
            var allocatedAmount = Math.Min(allocationTotal, payment.Amount);
            var advanceAmount = Math.Max(0, payment.Amount - allocatedAmount);

            var paymentJournalId = await journalPoster.PostPaymentAsync(
                new SupplierPaymentApprovalJournalRequest(
                    businessId,
                    payment.Id,
                    payment.OutletId,
                    payment.PaymentDate.Date,
                    payment.ReferenceNumber,
                    payment.Notes,
                    payment.PaymentAccountId,
                    postingAccounts.SupplierPayableAccountId,
                    allocatedAmount),
                actorUserId,
                cancellationToken);

            payment.AllocatedAmount = allocatedAmount;
            payment.AdvanceAmount = advanceAmount;
            payment.Status = "posted";
            payment.JournalEntryId = paymentJournalId;
            payment.ApprovedBy = actorUserId;
            payment.ApprovedAt = DateTime.UtcNow;
            payment.PostedBy = actorUserId;
            payment.PostedAt = DateTime.UtcNow;
            payment.UpdatedBy = actorUserId;
            payment.UpdatedAt = DateTime.UtcNow;

            var allocations = await dbContext.SupplierPaymentAllocations
                .Where(allocation => allocation.SupplierPaymentId == payment.Id)
                .ToListAsync(cancellationToken);
            foreach (var allocation in allocations)
            {
                allocation.Status = "effective";
                allocation.EffectiveDate = payment.PostingDate;
                allocation.JournalEntryId = paymentJournalId;
            }

            long? advanceJournalId = null;
            if (advanceAmount > 0)
            {
                advanceJournalId = await journalPoster.PostAdvanceAsync(
                    new SupplierPaymentApprovalJournalRequest(
                        businessId,
                        payment.Id,
                        payment.OutletId,
                        payment.PaymentDate.Date,
                        payment.ReferenceNumber,
                        payment.Notes,
                        payment.PaymentAccountId,
                        postingAccounts.SupplierAdvanceAccountId,
                        advanceAmount),
                    actorUserId,
                    cancellationToken);

                dbContext.SupplierAdvances.Add(new SupplierAdvance
                {
                    BusinessId = businessId,
                    OutletId = payment.OutletId,
                    SupplierId = payment.SupplierId,
                    PaymentDate = payment.PaymentDate,
                    BusinessDate = payment.PaymentDate,
                    PostingDate = payment.PaymentDate,
                    PaymentMethod = payment.PaymentMethod,
                    PaymentAccountId = payment.PaymentAccountId,
                    Amount = advanceAmount,
                    RemainingAmount = advanceAmount,
                    AppliedAmount = 0,
                    Status = "posted",
                    ReferenceNumber = payment.ReferenceNumber,
                    Notes = payment.Notes,
                    SourcePaymentId = payment.Id,
                    JournalEntryId = advanceJournalId,
                    CreatedBy = actorUserId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedBy = actorUserId,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordApprovedAsync(
                new SupplierPaymentApprovalAuditRecord(
                    payment.Id,
                    payment.BusinessId,
                    payment.OutletId,
                    payment.SupplierId,
                    payment.Amount,
                    payment.PaymentMethod,
                    payment.PaymentAccountId,
                    payment.JournalEntryId,
                    payment.ReversalJournalEntryId,
                    payment.AllocatedAmount,
                    payment.AdvanceAmount,
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

        return await FindByIdAsync(paymentId, cancellationToken);
    }

    private async Task<SupplierPaymentReadModel> FindByIdAsync(
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

    private static string BuildAllocationSummary(IEnumerable<SupplierPaymentAllocation> allocations) =>
        string.Join(
            ";",
            allocations.Select(allocation =>
                $"{allocation.PayableDocumentType}:{allocation.PayableDocumentId}:{allocation.AllocatedAmount:0.00}:{allocation.Status}"));
}
