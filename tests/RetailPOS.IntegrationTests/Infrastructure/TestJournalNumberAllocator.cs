using RetailPOS.Application.AccountingJournals.Abstractions;
using System.Collections.Concurrent;

namespace RetailPOS.Tests.Infrastructure;

public sealed class TestJournalNumberAllocator : IJournalNumberAllocator
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> AllocateNextJournalNumberAsync(long businessId, long? financialYearId, DateTime postingDate, long? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var yearToken = financialYearId?.ToString() ?? postingDate.Year.ToString();
        var key = $"{businessId}:{yearToken}";
        var next = _counters.AddOrUpdate(key, 1, (_, current) => current + 1);
        return Task.FromResult($"JR-{postingDate.Year}-{next.ToString().PadLeft(6, '0')}");
    }
}