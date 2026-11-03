using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierAdvanceApprovalStore(
    RetailPOSDbContext dbContext,
    ISupplierAdvanceApprovalJournalPoster journalPoster,
    ISupplierAdvanceApprovalAuditWriter auditWriter)
    : ISupplierAdvanceApprovalStore
{
    public Task<SupplierAdvanceApprovalSnapshot?> FindForApprovalAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierAdvances
            .AsNoTracking()
            .Where(advance => advance.Id == advanceId && advance.BusinessId == businessId)
            .Select(advance => new SupplierAdvanceApprovalSnapshot(
                advance.Id,
                advance.OutletId,
                advance.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SupplierAdvanceReadModel> ApproveAsync(
        long advanceId,
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
                    $"SELECT id FROM supplier_advances WHERE id = {advanceId} AND business_id = {businessId} FOR UPDATE",
                    cancellationToken);
            }

            var advance = await dbContext.SupplierAdvances
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == advanceId && candidate.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Supplier advance {advanceId} was not found.");

            if (!string.Equals(advance.Status, "submitted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only submitted supplier advances can be approved.");

            var paymentAccountExists = await dbContext.ChartAccounts.AnyAsync(
                account => account.BusinessId == businessId && account.Id == advance.PaymentAccountId,
                cancellationToken);
            if (!paymentAccountExists)
                throw new InvalidOperationException("Payment account was not found.");

            var advanceAccountId = await journalPoster.ResolveAdvanceAccountIdAsync(
                businessId,
                cancellationToken);
            var period = await dbContext.AccountingPeriods
                .Include(candidate => candidate.FinancialYear)
                .FirstOrDefaultAsync(
                    candidate => candidate.FinancialYear.BusinessId == businessId
                        && candidate.StartDate <= advance.PaymentDate.Date
                        && candidate.EndDate >= advance.PaymentDate.Date,
                    cancellationToken);
            if (period is null
                || !string.Equals(
                    period.Status,
                    AccountingPeriodStatuses.Open,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The accounting period is closed for this date.");

            var journalEntryId = await journalPoster.PostAsync(
                new SupplierAdvanceApprovalJournalRequest(
                    businessId,
                    advance.Id,
                    advance.OutletId,
                    advance.PaymentDate.Date,
                    advance.ReferenceNumber,
                    advance.Notes,
                    advanceAccountId,
                    advance.PaymentAccountId,
                    advance.Amount),
                actorUserId,
                cancellationToken);

            advance.Status = "posted";
            advance.JournalEntryId = journalEntryId;
            advance.ApprovedBy = actorUserId;
            advance.ApprovedAt = DateTime.UtcNow;
            advance.PostedBy = actorUserId;
            advance.PostedAt = DateTime.UtcNow;
            advance.UpdatedBy = actorUserId;
            advance.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordApprovedAsync(
                new SupplierAdvanceApprovalAuditRecord(
                    advance.Id,
                    advance.BusinessId,
                    advance.OutletId,
                    advance.SupplierId,
                    advance.Amount,
                    advance.RemainingAmount,
                    advance.AppliedAmount,
                    advance.PaymentMethod,
                    advance.PaymentAccountId,
                    advance.JournalEntryId,
                    advance.SourcePaymentId,
                    actorUserId),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        var approved = await dbContext.SupplierAdvances
            .AsNoTracking()
            .Include(candidate => candidate.Supplier)
            .FirstAsync(candidate => candidate.Id == advanceId && candidate.BusinessId == businessId, cancellationToken);

        return new SupplierAdvanceReadModel(
            approved.Id,
            approved.SupplierId,
            approved.Supplier.Name,
            approved.OutletId,
            approved.PaymentDate,
            approved.PaymentMethod,
            approved.PaymentAccountId,
            approved.Amount,
            approved.RemainingAmount,
            approved.AppliedAmount,
            approved.Status,
            approved.ReferenceNumber,
            approved.Notes,
            approved.JournalEntryId);
    }
}
