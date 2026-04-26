using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class CreateSaleItemDto
{
    [Required]
    public long VariantId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public int Quantity { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative")]
    public decimal UnitPrice { get; set; }

    // UPDATED — line-level discount captured from PricingService result (for audit trail)
    public decimal DiscountAmount { get; set; } = 0;

    // UPDATED — rule that was active at cart-price time (snapshot for receipts)
    public long?   AppliedRuleId   { get; set; }
    public string? AppliedRuleName { get; set; }
}
