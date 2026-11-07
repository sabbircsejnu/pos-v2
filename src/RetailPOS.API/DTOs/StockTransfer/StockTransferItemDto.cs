namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string? ProductCode { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public string? VariantDisplay { get; set; }
    public string? PrimaryImageThumb { get; set; }
    public decimal Quantity { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal TransferQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal InTransitQuantity { get; set; }
    public decimal ReturnTransitQuantity { get; set; }
    /// <summary>Null for a caller without products.view_cost.</summary>
    public decimal? UnitCost { get; set; }
    public string? Remarks { get; set; }
    public List<TransferAllocationDto>? Allocations { get; set; }
    public string? UomUnitCode { get; set; }
    public string? UomLabel { get; set; }
    public decimal? UomEnteredQuantity { get; set; }
}
