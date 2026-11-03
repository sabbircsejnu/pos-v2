namespace RetailPOS.Core.Entities;

public static class StockCountOperationOutcome
{
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
}

public sealed class StockCountOperationReceipt
{
    public long Id { get; set; }
    public long StockCountId { get; set; }
    public Guid ClientOperationId { get; set; }
    public string DeviceSessionId { get; set; } = string.Empty;
    public long ClientSequence { get; set; }
    public long ProductVariantId { get; set; }
    public string OperationType { get; set; } = "SetQuantity";
    public decimal CountedQuantity { get; set; }
    public string? RawBarcode { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public long ActorUserId { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public DateTime? ClientObservedAtUtc { get; set; }
    public DateTime ServerReceivedAtUtc { get; set; }
    public DateTime DecidedAtUtc { get; set; }
    public long? AcceptedRevision { get; set; }
    public long SessionRevisionAtDecision { get; set; }
    public string Outcome { get; set; } = StockCountOperationOutcome.Accepted;
    public string? RejectionCode { get; set; }
    public string CorrelationId { get; set; } = string.Empty;

    public StockCount StockCount { get; set; } = null!;
}
