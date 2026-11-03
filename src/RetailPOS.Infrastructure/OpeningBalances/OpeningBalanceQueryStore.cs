using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OpeningBalances;

public sealed class OpeningBalanceQueryStore(RetailPOSDbContext dbContext)
    : IOpeningBalanceQueryStore
{
    private static readonly HashSet<string> OpeningBalanceTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            AccountingJournalTypes.OpeningBalance,
            AccountingJournalTypes.OpeningBalanceAdjustment
        };

    public async Task<OpeningBalanceStatus> GetStatusAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var openingJournal = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.Status == JournalEntryStatuses.Posted
                && journal.JournalType == AccountingJournalTypes.OpeningBalance)
            .OrderBy(journal => journal.PostedAt ?? journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var lockingJournal = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.Status == JournalEntryStatuses.Posted
                && !OpeningBalanceTypes.Contains(journal.JournalType))
            .OrderBy(journal => journal.PostedAt ?? journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var hasOpening = openingJournal is not null;
        var isLocked = lockingJournal is not null;

        return new OpeningBalanceStatus(
            hasOpening,
            openingJournal?.Id,
            openingJournal?.JournalNumber,
            openingJournal?.PostedAt,
            isLocked,
            lockingJournal?.Id,
            lockingJournal?.JournalNumber,
            lockingJournal?.JournalType,
            lockingJournal?.PostedAt,
            !hasOpening && !isLocked);
    }
}
