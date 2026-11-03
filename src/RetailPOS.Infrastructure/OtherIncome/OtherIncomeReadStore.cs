using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OtherIncome;

public sealed class OtherIncomeReadStore(RetailPOSDbContext dbContext) : IOtherIncomeReadStore
{
    public async Task<OtherIncomeListReadModel> SearchAsync(
        OtherIncomeSearchFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OtherIncomeEntries
            .AsNoTracking()
            .Where(entry => entry.BusinessId == filter.BusinessId);

        if (filter.StartDate.HasValue)
        {
            query = query.Where(entry => entry.IncomeDate >= filter.StartDate.Value.Date);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(entry => entry.IncomeDate <= filter.EndDate.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim();
            query = query.Where(entry => entry.Status == status);
        }

        if (filter.OutletId.HasValue)
        {
            query = query.Where(entry => entry.OutletId == filter.OutletId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(entry => entry.IncomeDate)
            .ThenByDescending(entry => entry.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(entry => new OtherIncomeEntryReadModel(
                entry.Id,
                entry.BusinessId,
                entry.IncomeDate,
                entry.IncomeCategory,
                entry.IncomeAccountId,
                entry.Amount,
                entry.PaymentMethod,
                entry.PaymentAccountId,
                entry.ReferenceNumber,
                entry.Description,
                entry.Status,
                entry.OutletId,
                entry.JournalEntryId,
                entry.CreatedAt,
                entry.SubmittedAt,
                entry.ApprovedAt,
                entry.RejectedAt,
                entry.RejectionReason,
                entry.CancelledAt,
                entry.CancellationReason))
            .ToListAsync(cancellationToken);

        return new OtherIncomeListReadModel(items, totalCount, filter.PageNumber, filter.PageSize);
    }

    public async Task<OtherIncomeEntryReadModel> GetByIdAsync(
        long businessId,
        long entryId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OtherIncomeEntries
            .AsNoTracking()
            .Where(entry => entry.BusinessId == businessId && entry.Id == entryId)
            .Select(entry => new OtherIncomeEntryReadModel(
                entry.Id,
                entry.BusinessId,
                entry.IncomeDate,
                entry.IncomeCategory,
                entry.IncomeAccountId,
                entry.Amount,
                entry.PaymentMethod,
                entry.PaymentAccountId,
                entry.ReferenceNumber,
                entry.Description,
                entry.Status,
                entry.OutletId,
                entry.JournalEntryId,
                entry.CreatedAt,
                entry.SubmittedAt,
                entry.ApprovedAt,
                entry.RejectedAt,
                entry.RejectionReason,
                entry.CancelledAt,
                entry.CancellationReason))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Other income entry with ID {entryId} not found.");
    }
}
