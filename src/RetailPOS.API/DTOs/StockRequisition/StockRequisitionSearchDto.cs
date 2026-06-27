namespace RetailPOS.API.DTOs.StockRequisition;

public class StockRequisitionSearchDto
{
    public string? Status { get; set; }
    public long? RequestingLocationId { get; set; }
    public string? RequestingLocationType { get; set; }
    public long? SourceLocationId { get; set; }
    public string? SourceLocationType { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
