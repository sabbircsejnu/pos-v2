namespace RetailPOS.Core.Entities.Accounting;

public sealed class FrozenSystemJournalOperation
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string PostingReference { get; set; } = "";
    public string Kind { get; set; } = "";
    public string CanonicalPayloadJson { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public long JournalId { get; set; }
    public long? OriginalJournalId { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
