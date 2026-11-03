using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierAdvanceSubmissionStore(RetailPOSDbContext dbContext)
    : ISupplierAdvanceSubmissionStore
{
    public Task<SupplierAdvanceSubmissionSnapshot?> FindForSubmissionAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SupplierAdvances
            .AsNoTracking()
            .Where(advance => advance.Id == advanceId && advance.BusinessId == businessId)
            .Select(advance => new SupplierAdvanceSubmissionSnapshot(
                advance.Id,
                advance.OutletId,
                advance.Status,
                advance.SupplierId,
                advance.Amount,
                advance.RemainingAmount,
                advance.AppliedAmount,
                advance.PaymentMethod,
                advance.PaymentAccountId,
                advance.JournalEntryId,
                advance.SourcePaymentId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SubmitAsync(
        long advanceId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var advance = await dbContext.SupplierAdvances
            .FirstOrDefaultAsync(
                candidate => candidate.Id == advanceId && candidate.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier advance {advanceId} was not found.");

        advance.Status = "submitted";
        advance.SubmittedBy = actorUserId;
        advance.SubmittedAt = DateTime.UtcNow;
        advance.UpdatedBy = actorUserId;
        advance.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierAdvanceReadModel> FindByIdAsync(
        long advanceId,
        CancellationToken cancellationToken)
    {
        var advance = await dbContext.SupplierAdvances
            .AsNoTracking()
            .Include(candidate => candidate.Supplier)
            .FirstAsync(candidate => candidate.Id == advanceId, cancellationToken);

        return new SupplierAdvanceReadModel(
            advance.Id,
            advance.SupplierId,
            advance.Supplier.Name,
            advance.OutletId,
            advance.PaymentDate,
            advance.PaymentMethod,
            advance.PaymentAccountId,
            advance.Amount,
            advance.RemainingAmount,
            advance.AppliedAmount,
            advance.Status,
            advance.ReferenceNumber,
            advance.Notes,
            advance.JournalEntryId);
    }
}
