namespace RetailPOS.API.DTOs.StockRequisition;

public class CreateStockRequisitionDto
{
    public long RequestingLocationId { get; set; }
    public string RequestingLocationType { get; set; } = string.Empty;
    public long SourceLocationId { get; set; }
    public string SourceLocationType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<CreateStockRequisitionLineDto> Lines { get; set; } = new();
}

public class CreateStockRequisitionLineDto
{
    public long VariantId { get; set; }
    public int RequestedQuantity { get; set; }
    public string? Remarks { get; set; }
}
