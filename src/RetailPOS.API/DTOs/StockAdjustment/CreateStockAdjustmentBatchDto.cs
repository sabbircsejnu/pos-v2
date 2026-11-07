namespace RetailPOS.API.DTOs.StockAdjustment;

public class CreateStockAdjustmentBatchDto
{
    public string Action { get; set; } = StockAdjustmentCreateActions.Draft;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<CreateStockAdjustmentLineDto> Items { get; set; } = new();
}

public class CreateStockAdjustmentLineDto
{
    public long VariantId { get; set; }
    public decimal QuantityChange { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    /// <summary>Signed quantity as entered. With it the server converts to base units and ignores QuantityChange.</summary>
    public decimal? EnteredQuantity { get; set; }
    public long? UnitOptionId { get; set; }
    public long? ExpectedConfigVersionId { get; set; }
    public string? PackState { get; set; }
    public string? PackCode { get; set; }
    public decimal? BaseQtyPerPack { get; set; }
}

public static class StockAdjustmentCreateActions
{
    public const string Draft = "Draft";
    public const string Submit = "Submit";
    public const string SubmitAndApprove = "SubmitAndApprove";
}
