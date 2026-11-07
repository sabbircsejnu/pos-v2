namespace RetailPOS.Core.Entities;

public static class SaleStatuses
{
    public const string Completed = "completed";
    public const string Voided = "voided";

    /// <summary>Nothing is left to return. Sales refunded before multiple refunds also carry this status and stay closed.</summary>
    public const string Refunded = "refunded";

    /// <summary>At least one refund has been recorded and some quantity can still be returned.</summary>
    public const string PartiallyRefunded = "partially_refunded";
}

public class Sale
{
    public long Id { get; set; }

    // UPDATED — human-readable number printed on receipts (e.g. "S-20260317-00042")
    public string SaleNumber { get; set; } = string.Empty;

    public long OutletId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    public long? CustomerId { get; set; }
    public DateTime? BusinessDate { get; set; }
    public DateTime? SaleDateTime { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; } = 0;
    public decimal Tax { get; set; } = 0;

    /// <summary>
    /// Primary payment method — kept for backward compatibility and fast querying.
    /// When a sale uses split payments the full breakdown is in the Payments collection.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    public string Status { get; set; } = "completed";
    public long CashierId { get; set; }

    /// <summary>
    /// Phase 4: the eligible outlet user credited with the sale. Independent of the cashier (who is always the
    /// authenticated user). Null for sales created before Phase 4 or by clients that do not send it.
    /// </summary>
    public long? SalespersonUserId { get; set; }

    /// <summary>
    /// Client-supplied idempotency key (UUID) to prevent double-submission.
    /// Unique per outlet — the service rejects a second request with the same key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// SHA-256 (hex) of the canonical checkout request that produced this sale. A replay of the same
    /// idempotency key must carry the same fingerprint; a different payload is a conflict.
    /// </summary>
    public string? IdempotencyFingerprint { get; set; }

    /// <summary>Owning Business (derived from the outlet at creation). Null only for historical rows not yet backfilled.</summary>
    public long? BusinessId { get; set; }

    /// <summary>D13 daily sequence for (BusinessId, OutletId, BusinessDate); structured source of truth for the invoice number.</summary>
    public long? InvoiceSequence { get; set; }

    /// <summary>Outlet prefix as it was when the invoice number was issued.</summary>
    public string? InvoicePrefix { get; set; }

    public DateTime? InvoiceIssuedAtUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When present, this sale is a replacement for the referenced original sale.</summary>
    public long? OriginalSaleId { get; set; }

    /// <summary>When present, the original sale points to this replacement sale.</summary>
    public long? ReplacementSaleId { get; set; }

    // Navigation properties
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual PosTerminal? Terminal { get; set; }
    public virtual CashSession? CashSession { get; set; }
    public virtual Customer? Customer { get; set; }
    public virtual User Cashier { get; set; } = null!;
    public virtual User? Salesperson { get; set; }
    public virtual ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();

    // UPDATED — full split-payment breakdown (1 row per payment method used)
    public virtual ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
    public virtual ICollection<RetailPOS.Core.Entities.Uom.SaleRefund> Refunds { get; set; } = new List<RetailPOS.Core.Entities.Uom.SaleRefund>();
    public virtual ICollection<ReceiptPrintHistory> ReceiptPrintHistories { get; set; } = new List<ReceiptPrintHistory>();
    public virtual ICollection<SaleVoid> SaleVoids { get; set; } = new List<SaleVoid>();
}
