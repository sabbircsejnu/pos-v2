// =====================================================================
// NEW — Split-payment record for a single sale.
// One Sale can have many SalePayments (e.g. £30 cash + £20 card).
// The sum of all SalePayments.Amount must equal Sale.TotalAmount.
// =====================================================================
namespace RetailPOS.Core.Entities;

public class SalePayment
{
    public long Id { get; set; }
    public long SaleId { get; set; }

    /// <summary>"cash" | "card" | "mobile" | "loyalty" — mirrors existing PaymentMethod values.</summary>
    public string Method { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public long? PaymentAccountId { get; set; }

    /// <summary>For cash payments: the physical cash handed over by the customer (CashTendered).</summary>
    public decimal? Tendered { get; set; }

    /// <summary>Cash returned to the customer (Tendered − Amount). Immutable evidence; null for non-cash or legacy rows.</summary>
    public decimal? ChangeGiven { get; set; }

    /// <summary>Cash that stayed in the drawer for this allocation (equals Amount for cash). Drawer reconciliation uses this, never Tendered.</summary>
    public decimal? NetCashRetained { get; set; }

    // Navigation
    public virtual Sale Sale { get; set; } = null!;
}
