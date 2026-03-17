using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class CreateSaleItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
