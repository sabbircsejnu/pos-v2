// =====================================================================
// NEW — DTOs for the Hold / Park Sale feature.
// =====================================================================
using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>An item in a held cart (mirrors CreateSaleItemDto but without server-side validation).</summary>
public class HeldSaleItemDto
{
    public long    VariantId       { get; set; }
    public string  ProductName     { get; set; } = string.Empty;
    public string  VariantSku      { get; set; } = string.Empty;
    public int     Quantity        { get; set; }
    public decimal UnitPrice       { get; set; }
    public decimal DiscountAmount  { get; set; } = 0;
    public string? AppliedRuleName { get; set; }
}

public class HoldSaleDto
{
    [Required]
    public long OutletId { get; set; }

    [Required]
    public long CashierId { get; set; }

    public long? CustomerId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "A held sale must contain at least one item")]
    public List<HeldSaleItemDto> Items { get; set; } = new();

    public decimal DiscountPercent { get; set; } = 0;
    public string  PaymentMethod   { get; set; } = "cash";

    [MaxLength(300)]
    public string? Note { get; set; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class HeldSaleDto
{
    public long   Id              { get; set; }
    public long   OutletId        { get; set; }
    public string OutletName      { get; set; } = string.Empty;
    public long   CashierId       { get; set; }
    public string CashierName     { get; set; } = string.Empty;
    public long?  CustomerId      { get; set; }
    public string? CustomerName   { get; set; }
    public List<HeldSaleItemDto> Items { get; set; } = new();
    public decimal DiscountPercent { get; set; }
    public string  PaymentMethod   { get; set; } = string.Empty;
    public string? Note            { get; set; }
    public DateTime HeldAt         { get; set; }
    /// <summary>Pre-calculated cart subtotal (convenience, computed from Items).</summary>
    public decimal Subtotal        { get; set; }
}

public class HeldSaleListDto
{
    public List<HeldSaleDto> HeldSales { get; set; } = new();
    public int TotalCount { get; set; }
}
