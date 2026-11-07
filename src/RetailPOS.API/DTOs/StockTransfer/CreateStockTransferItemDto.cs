namespace RetailPOS.API.DTOs.StockTransfer;

public class CreateStockTransferItemDto
{
    public long VariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? RequestedQuantity { get; set; }
    public decimal? TransferQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Quantity as entered. With it the server converts to base units; the Quantity fields are ignored.</summary>
    public decimal? EnteredQuantity { get; set; }
    public long? UnitOptionId { get; set; }
    public long? ExpectedConfigVersionId { get; set; }
    public List<TransferAllocationDto>? Allocations { get; set; }
}

public class TransferAllocationDto
{
    public string State { get; set; } = string.Empty;
    public string? PackCode { get; set; }
    public decimal BaseQtyPerPack { get; set; }
    public int PackCount { get; set; }
    public decimal BaseQuantity { get; set; }
}
