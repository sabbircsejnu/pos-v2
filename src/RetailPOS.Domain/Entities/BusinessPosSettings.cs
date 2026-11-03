namespace RetailPOS.Core.Entities;

/// <summary>
/// Business-level POS configuration (D3, D9, P2.1, P2.2). One row per Business; a missing row means
/// "use the platform defaults" and is created on the first authorized change.
/// </summary>
public class BusinessPosSettings
{
    public const int MaxCashSessionDurationHoursLimit = 20;
    public const string DefaultTimeZone = "Asia/Dhaka";

    public long BusinessId { get; set; }

    /// <summary>Maximum Cash Session duration in hours; 1..20. Snapshotted when a session opens (Phase 3).</summary>
    public int MaxCashSessionDurationHours { get; set; } = MaxCashSessionDurationHoursLimit;

    /// <summary>Business default IANA time zone; an Outlet may override it.</summary>
    public string DefaultTimeZoneId { get; set; } = DefaultTimeZone;

    public bool TaxEnabled { get; set; } = true;

    /// <summary>Business default tax rate (percent) used when a product has no configured rate.</summary>
    public decimal DefaultTaxRate { get; set; } = 10m;
    public string TaxName { get; set; } = "VAT";

    /// <summary>True when selling prices already include tax.</summary>
    public bool PricesIncludeTax { get; set; }

    /// <summary>
    /// Highest manual (cashier-entered) discount, as a percentage of the pre-manual-discount amount, that may be
    /// applied without <c>sales.discount.override_limit</c>. 0 (the default) allows no manual discount.
    /// </summary>
    public decimal MaxManualDiscountPercent { get; set; }

    /// <summary>
    /// D7: when true, a Business Owner who also holds the explicit self-approval permission may approve their own
    /// POS Drawer / Petty Cash expense. Off by default; every use is audited.
    /// </summary>
    public bool AllowOwnerExpenseSelfApproval { get; set; }

    public long? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;

    public virtual Business Business { get; set; } = null!;
}

/// <summary>Per (Business, Outlet, BusinessDate) invoice counter (D13). Rows are only ever incremented.</summary>
public class SaleInvoiceCounter
{
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public DateTime BusinessDate { get; set; }
    public long LastSequence { get; set; }
}
