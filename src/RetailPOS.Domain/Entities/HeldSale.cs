// =====================================================================
// NEW — Held / parked sale entity.
// Allows a cashier to park the current cart and start a new transaction,
// then recall the parked cart to resume it.
//
// Held sales are purely a soft-state record: no inventory is reserved.
// Stock availability is re-validated when the held sale is resumed and
// submitted as a normal CreateSaleDto.
// =====================================================================
namespace RetailPOS.Core.Entities;

public class HeldSale
{
    public long Id { get; set; }

    /// <summary>Outlet where the sale was parked.</summary>
    public long OutletId { get; set; }

    /// <summary>Cashier who parked the sale.</summary>
    public long CashierId { get; set; }

    /// <summary>Optional customer attached at the time of hold.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Serialised cart items (JSON array of HeldSaleItem).</summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>Header-level discount percentage captured at hold time.</summary>
    public decimal DiscountPercent { get; set; } = 0;

    /// <summary>Preferred payment method captured at hold time (may change on resume).</summary>
    public string PaymentMethod { get; set; } = "cash";

    /// <summary>Optional cashier note shown on the parked-sales list.</summary>
    public string? Note { get; set; }

    public DateTime HeldAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual User Cashier { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
}
