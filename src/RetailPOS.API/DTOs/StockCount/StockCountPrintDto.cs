namespace RetailPOS.API.DTOs.StockCount;

public class StockCountPrintDto
{
    public long Id { get; set; }
    public string StockCountNo { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public DateTime StockCountDate { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool HasPostGenerationMovements { get; set; }
    public int PostGenerationMovementCount { get; set; }
    public DateTime? LastPostGenerationMovementAt { get; set; }
    public List<StockCountLineDto> Lines { get; set; } = new();
}
