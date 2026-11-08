using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Core.Entities.Exchanges;

/// <summary>
/// Append-only, business-scoped exchange policy (D-16). No row means the module is not configured for the business.
/// A completed exchange keeps the version it applied as an immutable snapshot.
/// </summary>
public class ExchangePolicyVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public int VersionNo { get; set; }
    public bool IsEnabled { get; set; }
    public bool AllowLowerPriceExchange { get; set; }
    public int ExchangeWindowDays { get; set; }
    public bool AllowReplacementReExchange { get; set; }
    public int? ReplacementWindowDays { get; set; }
    public int? MaxExchangeCountPerLineage { get; set; }
    public string PhoneVerificationMode { get; set; } = "optional";
    public decimal? HighValueApprovalThreshold { get; set; }
    public string NegativeTargetAdjustmentMode { get; set; } = "track_only";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Daily exchange number sequence per (business, receiving outlet, cash-session business date) (D-19).</summary>
public class ExchangeNumberCounter
{
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public DateTime BusinessDate { get; set; }
    public long LastSequence { get; set; }
}

/// <summary>
/// A completed product exchange (EX-01–EX-31). Immutable once completed: only <see cref="Status"/> and
/// <see cref="ReversalId"/> may change, and only through a controlled reversal (EX-14).
/// </summary>
public class Exchange
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ExchangeNumber { get; set; } = string.Empty;
    public long ReceivingOutletId { get; set; }
    public long TerminalId { get; set; }
    public long CashSessionId { get; set; }

    /// <summary>Cash-session business date: drives numbering, accounting and reporting (EX-01, D-19).</summary>
    public DateTime BusinessDate { get; set; }

    /// <summary>Local calendar date of confirmation in <see cref="TimeZoneId"/>: drives eligibility windows (OC-07).</summary>
    public DateTime EligibilityLocalDate { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public long CashierUserId { get; set; }
    public long? SalespersonUserId { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = "completed";
    public string ReasonCode { get; set; } = string.Empty;
    public string? ReasonOtherText { get; set; }
    public string? InternalNote { get; set; }
    public string PhoneVerificationResult { get; set; } = "not_required";

    public decimal ReturnNet { get; set; }
    public decimal ReturnVat { get; set; }
    public decimal ReturnGross { get; set; }
    public decimal ReturnCost { get; set; }
    public decimal ReplacementNet { get; set; }
    public decimal ReplacementDiscount { get; set; }
    public decimal ReplacementVat { get; set; }
    public decimal ReplacementGross { get; set; }
    public decimal ReplacementCost { get; set; }
    public decimal NetAdjustmentNet { get; set; }
    public decimal NetAdjustmentVat { get; set; }
    public decimal NetAdjustmentGross { get; set; }
    public decimal HighValueAmount { get; set; }
    public decimal? HighValueThresholdApplied { get; set; }
    public string SettlementDirection { get; set; } = "none";
    public decimal SettlementAmount { get; set; }

    public long PolicyVersionId { get; set; }
    public string PolicySnapshot { get; set; } = "{}";
    public string PreviewHash { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }

    /// <summary>D-05: always <c>module_inactive</c> until the Loyalty module exists (EXL-01).</summary>
    public string LoyaltyStatus { get; set; } = "module_inactive";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public long? ReversalId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ExchangeSourceInvoice> SourceInvoices { get; set; } = new List<ExchangeSourceInvoice>();
    public ICollection<ExchangeReturnLine> ReturnLines { get; set; } = new List<ExchangeReturnLine>();
    public ICollection<ExchangeReplacementLine> ReplacementLines { get; set; } = new List<ExchangeReplacementLine>();
    public ICollection<ExchangeSettlement> Settlements { get; set; } = new List<ExchangeSettlement>();
    public ICollection<ExchangeApproval> Approvals { get; set; } = new List<ExchangeApproval>();
}

public class ExchangeSourceInvoice
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public long SaleId { get; set; }
    public long OriginalOutletId { get; set; }
    public long? CustomerId { get; set; }
    public string InvoiceNumberSnapshot { get; set; } = string.Empty;
    public DateTime? SaleBusinessDateSnapshot { get; set; }
    public DateTime PurchaseLocalDate { get; set; }
}

/// <summary>A returned quantity: of an original sale line, or of an earlier exchange's replacement line (EX-04).</summary>
public class ExchangeReturnLine : UomSnapshotColumns
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public string SourceKind { get; set; } = "sale_item";
    public long? SaleId { get; set; }
    public long? SaleItemId { get; set; }
    public long? SourceReplacementLineId { get; set; }
    public long RootSaleItemId { get; set; }
    public int LineageDepth { get; set; }
    public long VariantId { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal CreditNet { get; set; }
    public decimal CreditVat { get; set; }
    public decimal CreditGross { get; set; }
    public decimal CostRestored { get; set; }
    public string Condition { get; set; } = "resellable";
    public bool PackagingReusable { get; set; }
}

/// <summary>An issued replacement, priced at the exchange date (EX-10). Its own returned quantity bounds re-exchange (C-03).</summary>
public class ExchangeReplacementLine : UomSnapshotColumns
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public long VariantId { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal InvoiceDiscountShare { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public string? TaxClassification { get; set; }
    public bool TaxInclusive { get; set; }
    public decimal HistoricalUnitCost { get; set; }
    public decimal PackagingCost { get; set; }
    public string? PackState { get; set; }
    public string? PackCode { get; set; }
    public decimal? BaseQtyPerPack { get; set; }
    public int LineageDepth { get; set; }
    public long? AppliedRuleId { get; set; }
    public string? AppliedRuleName { get; set; }

    public Exchange? Exchange { get; set; }
    public ICollection<ExchangeReplacementLineComponent> Components { get; set; } = new List<ExchangeReplacementLineComponent>();
}

public class ExchangeReplacementLineComponent
{
    public long Id { get; set; }
    public long ReplacementLineId { get; set; }
    public long ComponentVariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Many-to-many lineage of one exchange: every return line × every replacement line (LLD §3.8).</summary>
public class ExchangeLineageLink
{
    public long ReturnLineId { get; set; }
    public long ReplacementLineId { get; set; }
}

public class ExchangeSettlement
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public long? ExchangeReversalId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Tendered { get; set; }
    public decimal? ChangeGiven { get; set; }
    public long CashSessionId { get; set; }
    public long? CashSessionMovementId { get; set; }
    public string? ExternalReference { get; set; }
    public long? ExternalConfirmedByUserId { get; set; }
    public DateTime? ExternalConfirmedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A one-time supervisor approval granted at the cashier's counter (D-04, C-02, OC-01–OC-03, C-10). The token is
/// stored only as a hash; credentials are never stored.
/// </summary>
public class ExchangeApproval
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public long ReceivingOutletId { get; set; }
    public long? TerminalId { get; set; }
    public long CashierUserId { get; set; }
    public long SupervisorUserId { get; set; }
    public string PermissionUsed { get; set; } = string.Empty;
    public string SupervisorOutletAccessBasis { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public Guid AuthenticationGroupId { get; set; }
    public string Status { get; set; } = "granted";
    public DateTime ExpiresAtUtc { get; set; }
    public long? ConsumedByExchangeId { get; set; }
    public long? ConsumedByReversalId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}

/// <summary>Immutable salesperson attribution fact on the exchange (or reversal) business date (D-06, D-07).</summary>
public class ExchangeSalespersonAdjustment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExchangeId { get; set; }
    public long? ExchangeReversalId { get; set; }
    public DateTime BusinessDate { get; set; }
    public long ReceivingOutletId { get; set; }
    public long CashierUserId { get; set; }
    public long? SalespersonUserId { get; set; }
    public decimal PositiveAmountNet { get; set; }
    public decimal NegativeAmountNet { get; set; }
    public decimal SignedAmountNet { get; set; }
    public decimal TargetEffectAmountNet { get; set; }
    public string NegativeMode { get; set; } = "track_only";
    public long PolicyVersionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ExchangeReceiptSnapshot
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public long? ExchangeReversalId { get; set; }
    public int DocumentVersion { get; set; }
    public string Document { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}

public class ExchangeReceiptPrintHistory
{
    public long Id { get; set; }
    public long ExchangeId { get; set; }
    public long? TerminalId { get; set; }
    public long? PrintedByUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public DateTime PrintedAtUtc { get; set; }
}

/// <summary>A controlled, compensating reversal of a whole exchange (EX-14, C-05). At most one per exchange.</summary>
public class ExchangeReversal
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExchangeId { get; set; }
    public string ReversalNumber { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public long ReceivingOutletId { get; set; }
    public long TerminalId { get; set; }
    public long CashSessionId { get; set; }
    public long ActorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal NetAdjustmentGross { get; set; }
    /// <summary>Cost of packaging issued in a fraction that could not come back (I-07); it stays in COGS (L4).</summary>
    public decimal RetainedPackagingCost { get; set; }
    public long? JournalEntryId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
}

/// <summary>
/// One failed supervisor approval attempt (L1b). Only a SHA-256 subject key is kept: the supervisor login as typed
/// (normalised) or the requesting cashier, always within one business. Counted over a short window to lock out guessing.
/// </summary>
public class ExchangeApprovalFailure
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string SubjectKind { get; set; } = string.Empty;
    public string SubjectHash { get; set; } = string.Empty;
    public DateTime AttemptedAtUtc { get; set; }
}
