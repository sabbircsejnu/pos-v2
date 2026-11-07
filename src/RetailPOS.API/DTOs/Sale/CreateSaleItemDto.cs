using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class CreateSaleItemDto
{
    [Required]
    public long VariantId { get; set; }

    [Required]
    [Range(typeof(decimal), "0.000001", "2147483647", ErrorMessage = "Quantity must be greater than zero")]
    public decimal Quantity { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative")]
    public decimal UnitPrice { get; set; }

    // UPDATED — line-level discount captured from PricingService result (for audit trail)
    public decimal DiscountAmount { get; set; } = 0;

    // UPDATED — rule that was active at cart-price time (snapshot for receipts)
    public long?   AppliedRuleId   { get; set; }
    public string? AppliedRuleName { get; set; }

    /// <summary>The chosen pack or custom option. With one, Quantity is a pack count or a measured quantity of that option.</summary>
    public long? SellingOptionId { get; set; }
    public long? ExpectedConfigVersionId { get; set; }
    /// <summary>loose, sealed or prepared, for items that track pack state.</summary>
    public string? FulfilmentState { get; set; }
    public bool IncludePackaging { get; set; }

    /// <summary>The line amount the cashier saw. When sent, a promotion that changed since then refuses the sale instead of repricing it.</summary>
    public decimal? ExpectedLineGross { get; set; }
}
