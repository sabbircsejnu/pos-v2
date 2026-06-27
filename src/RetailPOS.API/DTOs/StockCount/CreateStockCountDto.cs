using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.StockCount;

public class CreateStockCountDto
{
    [Required]
    public DateTime StockCountDate { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    [StringLength(1000)]
    public string? Remarks { get; set; }
}
