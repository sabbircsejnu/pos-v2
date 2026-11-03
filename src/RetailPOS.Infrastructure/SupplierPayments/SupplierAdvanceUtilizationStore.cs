using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierAdvanceUtilizationStore(
    RetailPOSDbContext dbContext,
    ISupplierAdvanceUtilizationJournalPoster journalPoster,
    ISupplierPaymentReversalJournalPoster reversalJournalPoster,
    ISupplierAdvanceUtilizationAuditWriter auditWriter)
    : ISupplierAdvanceUtilizationStore
{
    public Task<SupplierAdvanceUtilizationAdvanceSnapshot?> FindPostedAdvanceAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierAdvances
            .AsNoTracking()
            .Where(advance => advance.Id == advanceId
                && advance.BusinessId == businessId
                && advance.Status == "posted")
            .Select(advance => new SupplierAdvanceUtilizationAdvanceSnapshot(
                advance.Id,
                advance.OutletId,
                advance.SupplierId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SupplierAdvanceUtilizationPayableSnapshot?> FindPayableAsync(
        long payableDocumentId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierPayableDocuments
            .AsNoTracking()
            .Where(payable => payable.Id == payableDocumentId
                && payable.BusinessId == businessId)
            .Select(payable => new SupplierAdvanceUtilizationPayableSnapshot(
                payable.Id,
                payable.SupplierId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SupplierAdvanceUtilizationReversalScope?> FindForReversalAsync(
        long utilizationId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierAdvanceUtilizations
            .AsNoTracking()
            .Where(utilization => utilization.Id == utilizationId
                && utilization.BusinessId == businessId
                && utilization.Status == "effective")
            .Select(utilization => new SupplierAdvanceUtilizationReversalScope(
                utilization.SupplierAdvanceId,
                utilization.SupplierPayableDocumentId,
                utilization.JournalEntryId,
                utilization.SupplierAdvance.OutletId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task ApplyAsync(
        long advanceId,
        long payableDocumentId,
        long businessId,
        decimal amount,
        DateTime effectiveDate,
        string referenceNumber,
        string? notes,
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
                    $"SELECT id FROM supplier_advances WHERE id = {advanceId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM supplier_payable_documents WHERE id = {payableDocumentId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
            }

            var advance = await dbContext.SupplierAdvances
                .FirstOrDefaultAsync(candidate => candidate.Id == advanceId
                    && candidate.BusinessId == businessId
                    && candidate.Status == "posted", cancellationToken)
                ?? throw new KeyNotFoundException($"Posted supplier advance {advanceId} was not found.");
            var payable = await dbContext.SupplierPayableDocuments
                .FirstOrDefaultAsync(candidate => candidate.Id == payableDocumentId
                    && candidate.BusinessId == businessId, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Supplier payable document {payableDocumentId} was not found.");
            if (advance.SupplierId != payable.SupplierId)
                throw new InvalidOperationException(
                    "Supplier advance and payable document must belong to the same supplier.");

            var period = await dbContext.AccountingPeriods
                .Include(candidate => candidate.FinancialYear)
                .FirstOrDefaultAsync(candidate => candidate.FinancialYear.BusinessId == businessId
                    && candidate.StartDate <= effectiveDate.Date
                    && candidate.EndDate >= effectiveDate.Date, cancellationToken);
            if (period is null
                || !string.Equals(period.Status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The accounting period is closed for this date.");

            var available = advance.Amount - await dbContext.SupplierAdvanceUtilizations
                .Where(utilization => utilization.SupplierAdvanceId == advance.Id
                    && utilization.Status == "effective")
                .SumAsync(utilization => (decimal?)utilization.Amount, cancellationToken) ?? 0m;
            var outstanding = await GetPayableOutstandingAsync(payable.Id, null, cancellationToken);
            if (amount > available)
                throw new InvalidOperationException(
                    "Advance application cannot exceed the available advance amount.");
            if (amount > outstanding)
                throw new InvalidOperationException(
                    "Advance application cannot exceed the payable outstanding amount.");

            var accounts = await journalPoster.ResolvePostingAccountsAsync(cancellationToken);
            var journalEntryId = await journalPoster.PostAsync(
                new SupplierAdvanceUtilizationJournalRequest(
                    businessId,
                    advance.Id,
                    payable.Id,
                    effectiveDate.Date,
                    referenceNumber,
                    notes,
                    accounts.SupplierPayableAccountId,
                    accounts.SupplierAdvanceAccountId,
                    amount),
                actorUserId,
                cancellationToken);

            if (!await dbContext.SupplierAdvanceUtilizations.AnyAsync(
                    utilization => utilization.BusinessId == businessId
                        && utilization.JournalEntryId == journalEntryId,
                    cancellationToken))
            {
                var utilization = new SupplierAdvanceUtilization
                {
                    BusinessId = businessId,
                    SupplierAdvanceId = advance.Id,
                    SupplierPayableDocumentId = payable.Id,
                    Amount = amount,
                    EffectiveDate = effectiveDate.Date,
                    JournalEntryId = journalEntryId,
                    Reason = notes,
                    CreatedBy = actorUserId
                };
                dbContext.SupplierAdvanceUtilizations.Add(utilization);
                advance.RemainingAmount -= amount;
                advance.AppliedAmount += amount;
                payable.Status = await GetPayableOutstandingAsync(
                    payable.Id,
                    amount,
                    cancellationToken) == 0
                    ? SupplierPayableDocumentStatuses.Paid
                    : SupplierPayableDocumentStatuses.Partial;
                await dbContext.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAppliedAsync(
                    new SupplierAdvanceUtilizationAuditRecord(
                        utilization.Id,
                        utilization.BusinessId,
                        utilization.SupplierAdvanceId,
                        utilization.SupplierPayableDocumentId,
                        utilization.Amount,
                        utilization.JournalEntryId,
                        actorUserId,
                        utilization.Reason),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ReverseAsync(
        long utilizationId,
        long businessId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var period = await dbContext.AccountingPeriods
            .Include(candidate => candidate.FinancialYear)
            .FirstOrDefaultAsync(candidate => candidate.FinancialYear.BusinessId == businessId
                && candidate.StartDate <= postingDate.Date
                && candidate.EndDate >= postingDate.Date, cancellationToken);
        if (period is null
            || !string.Equals(period.Status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The accounting period is closed for this date.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var scope = await dbContext.SupplierAdvanceUtilizations
                .AsNoTracking()
                .Where(utilization => utilization.Id == utilizationId
                    && utilization.BusinessId == businessId
                    && utilization.Status == "effective")
                .Select(utilization => new SupplierAdvanceUtilizationReversalScope(
                    utilization.SupplierAdvanceId,
                    utilization.SupplierPayableDocumentId,
                    utilization.JournalEntryId,
                    utilization.SupplierAdvance.OutletId))
                .FirstOrDefaultAsync(cancellationToken);

            if (scope is null)
                throw new InvalidOperationException("Only posted journals can be reversed.");

            if (string.Equals(
                    dbContext.Database.ProviderName,
                    "Npgsql.EntityFrameworkCore.PostgreSQL",
                    StringComparison.Ordinal))
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM supplier_advances WHERE id = {scope.AdvanceId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM supplier_payable_documents WHERE id = {scope.PayableDocumentId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM supplier_advance_utilizations WHERE id = {utilizationId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
            }

            var utilization = await dbContext.SupplierAdvanceUtilizations
                .Include(candidate => candidate.SupplierAdvance)
                .Include(candidate => candidate.SupplierPayableDocument)
                .FirstOrDefaultAsync(candidate => candidate.Id == utilizationId
                    && candidate.BusinessId == businessId
                    && candidate.Status == "effective", cancellationToken);

            if (utilization is null)
                throw new InvalidOperationException("Only posted journals can be reversed.");

            var reversalJournalId = await reversalJournalPoster.ReverseJournalAsync(
                businessId,
                utilization.JournalEntryId,
                postingDate.Date,
                reason,
                actorUserId,
                cancellationToken);

            utilization.Status = "reversed";
            utilization.SupplierAdvance.RemainingAmount += utilization.Amount;
            utilization.SupplierAdvance.AppliedAmount -= utilization.Amount;
            utilization.SupplierPayableDocument.Status = SupplierPayableDocumentStatuses.Open;
            dbContext.SupplierAdvanceUtilizations.Add(new SupplierAdvanceUtilization
            {
                BusinessId = businessId,
                SupplierAdvanceId = utilization.SupplierAdvanceId,
                SupplierPayableDocumentId = utilization.SupplierPayableDocumentId,
                Amount = utilization.Amount,
                EffectiveDate = postingDate.Date,
                JournalEntryId = reversalJournalId,
                Status = "reversal",
                ReversalOfUtilizationId = utilization.Id,
                Reason = reason,
                CreatedBy = actorUserId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordReversedAsync(
                new SupplierAdvanceUtilizationReversalAuditRecord(
                    utilization.Id,
                    utilization.BusinessId,
                    utilization.SupplierAdvanceId,
                    utilization.SupplierPayableDocumentId,
                    utilization.Amount,
                    utilization.JournalEntryId,
                    reversalJournalId,
                    reason,
                    actorUserId),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<decimal> GetPayableOutstandingAsync(
        long payableDocumentId,
        decimal? pendingApplicationAmount,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.SupplierPayableDocuments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == payableDocumentId, cancellationToken);
        var paymentAllocations = await dbContext.SupplierPaymentAllocations
            .Where(allocation => allocation.SupplierPayableDocumentId == payableDocumentId
                && allocation.Status == "effective"
                && !allocation.IsReversed)
            .SumAsync(allocation => (decimal?)allocation.AllocatedAmount, cancellationToken) ?? 0m;
        var utilizations = await dbContext.SupplierAdvanceUtilizations
            .Where(utilization => utilization.SupplierPayableDocumentId == payableDocumentId
                && utilization.Status == "effective")
            .SumAsync(utilization => (decimal?)utilization.Amount, cancellationToken) ?? 0m;
        return document.OriginalPayableAmount
            - paymentAllocations
            - utilizations
            - (pendingApplicationAmount ?? 0m);
    }
}
