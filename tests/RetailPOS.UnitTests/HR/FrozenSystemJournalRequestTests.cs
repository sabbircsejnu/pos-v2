using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.UnitTests.HR;

public sealed class FrozenSystemJournalRequestTests
{
    [Fact]
    public void HashIncludesMoneyDimensionsBusinessAndReference_AndNormalizesDecimalScale()
    {
        var input = Input(10m);
        var first = FrozenSystemJournalRequest.ForPosting(1, "foundation:1", input);
        Assert.Equal(first.PayloadHash, FrozenSystemJournalRequest.ForPosting(1, "foundation:1", Input(10.00m)).PayloadHash);
        Assert.NotEqual(first.PayloadHash, FrozenSystemJournalRequest.ForPosting(1, "foundation:1", Input(11m)).PayloadHash);
        Assert.NotEqual(first.PayloadHash, FrozenSystemJournalRequest.ForPosting(2, "foundation:1", input).PayloadHash);
        Assert.NotEqual(first.PayloadHash, FrozenSystemJournalRequest.ForPosting(1, "foundation:1", input with { OutletId = 3 }).PayloadHash);
    }

    [Fact]
    public void FreezeDoesNotFollowLaterMutableLineChanges()
    {
        var lines = Input(10).Lines.ToList();
        var frozen = FrozenSystemJournalRequest.ForPosting(1, "foundation:1", Input(10) with { Lines = lines });
        lines[0] = lines[0] with { DebitAmount = 20 };
        Assert.Equal(10, frozen.ReadFrozenPayload<ManualJournalDraftInput>().Lines[0].DebitAmount);
    }

    [Fact]
    public void CorrectionRequiresExplicitDatesAndStableOriginalLink()
    {
        Assert.Throws<ArgumentException>(() => FrozenSystemJournalRequest.ForReversal(1, "correction:1", 42,
            new("reason", null, null)));
        var date = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var first = FrozenSystemJournalRequest.ForReversal(1, "correction:1", 42, new("reason", date, date));
        Assert.NotEqual(first.PayloadHash, FrozenSystemJournalRequest.ForReversal(1, "correction:1", 43,
            new("reason", date, date)).PayloadHash);
    }

    private static ManualJournalDraftInput Input(decimal amount)
    {
        var date = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        return new("SalesInvoice", date, date, date, "SalesInvoice", "foundation:1", null, "Foundation",
            null, null, null, null, [new(1, null, amount, 0, null, null, null, null, null, null),
                new(2, null, 0, amount, null, null, null, null, null, null)]);
    }
}
