namespace RetailPOS.API.DTOs.StockRequisition;

public class StockRequisitionListDto
{
    public List<StockRequisitionDto> StockRequisitions { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
