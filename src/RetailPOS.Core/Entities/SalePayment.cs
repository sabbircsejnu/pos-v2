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

    /// <summary>For cash payments: the denomination handed over (used to calculate change in receipt).</summary>
    public decimal? Tendered { get; set; }

    // Navigation
    public virtual Sale Sale { get; set; } = null!;
}
