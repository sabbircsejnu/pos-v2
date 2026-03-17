namespace RetailPOS.Core.Entities;

public class Inventory
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty; // 'outlet' or 'warehouse'
    public int Quantity { get; set; } = 0;
    public int LowStockThreshold { get; set; } = 10;
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    // Navigation properties
    public virtual ProductVariant Variant { get; set; } = null!;
}
