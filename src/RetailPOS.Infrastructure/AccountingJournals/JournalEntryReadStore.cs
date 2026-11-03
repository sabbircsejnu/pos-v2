using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class JournalEntryReadStore(RetailPOSDbContext dbContext) : IJournalEntryReadStore
{
    public async Task<JournalListReadModel> SearchAsync(
        JournalSearchFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .Where(entry => entry.BusinessId == filter.BusinessId);

        if (filter.PostingDateStart.HasValue)
        {
            query = query.Where(entry => entry.PostingDate >= filter.PostingDateStart.Value.Date);
        }

        if (filter.PostingDateEnd.HasValue)
        {
            query = query.Where(entry => entry.PostingDate <= filter.PostingDateEnd.Value.Date);
        }

        if (filter.BusinessDateStart.HasValue)
        {
            query = query.Where(entry =>
                entry.BusinessDate.HasValue
                && entry.BusinessDate.Value >= filter.BusinessDateStart.Value.Date);
        }

        if (filter.BusinessDateEnd.HasValue)
        {
            query = query.Where(entry =>
                entry.BusinessDate.HasValue
                && entry.BusinessDate.Value <= filter.BusinessDateEnd.Value.Date);
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

        if (!string.IsNullOrWhiteSpace(filter.JournalType))
        {
            var journalType = filter.JournalType.Trim();
            query = query.Where(entry => entry.JournalType == journalType);
        }

        if (!string.IsNullOrWhiteSpace(filter.JournalNumber))
        {
            var number = filter.JournalNumber.Trim();
            query = query.Where(entry =>
                entry.JournalNumber != null
                && EF.Functions.ILike(entry.JournalNumber, $"%{number}%"));
        }

        if (filter.AccountId.HasValue)
        {
            query = query.Where(entry => entry.Lines.Any(line => line.AccountId == filter.AccountId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.ReferenceType))
        {
            var referenceType = filter.ReferenceType.Trim();
            query = query.Where(entry => entry.ReferenceType == referenceType);
        }

        if (!string.IsNullOrWhiteSpace(filter.ReferenceNumber))
        {
            var referenceNumber = filter.ReferenceNumber.Trim();
            query = query.Where(entry =>
                entry.ReferenceNumber != null
                && EF.Functions.ILike(entry.ReferenceNumber, $"%{referenceNumber}%"));
        }

        if (filter.CreatedBy.HasValue)
        {
            query = query.Where(entry => entry.CreatedBy == filter.CreatedBy.Value);
        }

        if (filter.PostedBy.HasValue)
        {
            query = query.Where(entry => entry.PostedBy == filter.PostedBy.Value);
        }

        var pageNumber = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        query = ApplySort(query, filter.SortBy, filter.SortDirection);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new JournalListReadModel(
            rows.Select(ToReadModel).ToList(),
            totalCount,
            pageNumber,
            pageSize);
    }

    public async Task<JournalEntryReadModel> GetByIdAsync(
        long businessId,
        long journalEntryId,
        CancellationToken cancellationToken)
    {
        var journal = await dbContext.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == journalEntryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Journal entry with ID {journalEntryId} was not found.");

        return ToReadModel(journal);
    }

    private static IOrderedQueryable<JournalEntry> ApplySort(
        IQueryable<JournalEntry> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var key = sortBy?.Trim().ToLowerInvariant();

        return key switch
        {
            "journalnumber" => descending
                ? query.OrderByDescending(entry => entry.JournalNumber).ThenByDescending(entry => entry.Id)
                : query.OrderBy(entry => entry.JournalNumber).ThenBy(entry => entry.Id),
            "businessdate" => descending
                ? query.OrderByDescending(entry => entry.BusinessDate).ThenByDescending(entry => entry.Id)
                : query.OrderBy(entry => entry.BusinessDate).ThenBy(entry => entry.Id),
            "createdat" => descending
                ? query.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
                : query.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id),
            _ => descending
                ? query.OrderByDescending(entry => entry.PostingDate).ThenByDescending(entry => entry.Id)
                : query.OrderBy(entry => entry.PostingDate).ThenBy(entry => entry.Id)
        };
    }

    private static JournalEntryReadModel ToReadModel(JournalEntry entry) =>
        new(
            entry.Id,
            entry.BusinessId,
            entry.JournalNumber,
            entry.JournalType,
            entry.BusinessDate,
            entry.TransactionDate,
            entry.PostingDate,
            entry.FinancialYearId,
            entry.AccountingPeriodId,
            entry.ReferenceType,
            entry.ReferenceId,
            entry.ReferenceNumber,
            entry.Description,
            entry.OutletId,
            entry.WarehouseId,
            entry.TerminalId,
            entry.CashSessionId,
            entry.Status,
            entry.IsSystemGenerated,
            entry.ReversalOfJournalEntryId,
            entry.ReversedByJournalEntryId,
            entry.SubmittedAt,
            entry.SubmittedBy,
            entry.ApprovedAt,
            entry.ApprovedBy,
            entry.RejectedAt,
            entry.RejectedBy,
            entry.RejectionReason,
            entry.CreatedAt,
            entry.CreatedBy,
            entry.UpdatedAt,
            entry.UpdatedBy,
            entry.PostedAt,
            entry.PostedBy,
            entry.CancelledAt,
            entry.CancelledBy,
            entry.CancellationReason,
            entry.ReversedAt,
            entry.ReversedBy,
            entry.Lines
                .OrderBy(line => line.Id)
                .Select(line => new JournalEntryLineReadModel(
                    line.Id,
                    line.AccountId,
                    line.Description,
                    line.DebitAmount,
                    line.CreditAmount,
                    line.OutletId,
                    line.WarehouseId,
                    line.SupplierId,
                    line.CustomerId,
                    line.PaymentMethodId,
                    line.CostCenterId))
                .ToList(),
            entry.Attachments
                .OrderBy(attachment => attachment.Id)
                .Select(attachment => new JournalEntryAttachmentReadModel(
                    attachment.Id,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.FileSizeBytes,
                    attachment.CreatedAt,
                    attachment.CreatedBy))
                .ToList());
}
