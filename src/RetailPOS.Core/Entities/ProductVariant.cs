namespace RetailPOS.Core.Entities;

public class ProductVariant
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Attributes { get; set; } = "{}"; // JSON object as string
    public decimal PriceAdjustment { get; set; } = 0;
    public decimal CostAdjustment { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<ProductVariantOption> ProductVariantOptions { get; set; } = new List<ProductVariantOption>();
    public virtual ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();
    public virtual ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public virtual ICollection<StockTransferItem> StockTransferItems { get; set; } = new List<StockTransferItem>();
    public virtual ICollection<StockAdjustment> StockAdjustments { get; set; } = new List<StockAdjustment>();
}
