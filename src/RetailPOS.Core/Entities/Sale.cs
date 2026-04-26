namespace RetailPOS.Core.Entities;

public class Sale
{
    public long Id { get; set; }

    // UPDATED — human-readable number printed on receipts (e.g. "S-20260317-00042")
    public string SaleNumber { get; set; } = string.Empty;

    public long OutletId { get; set; }
    public long? CustomerId { get; set; }
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
    /// Client-supplied idempotency key (UUID) to prevent double-submission.
    /// Unique per outlet — the service rejects a second request with the same key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual User Cashier { get; set; } = null!;
    public virtual ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();

    // UPDATED — full split-payment breakdown (1 row per payment method used)
    public virtual ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}
