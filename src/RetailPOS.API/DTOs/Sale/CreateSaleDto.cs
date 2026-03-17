using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class CreateSaleDto
{
    [Required]
    public long OutletId { get; set; }

    public long? CustomerId { get; set; }

    [Required]
    public List<CreateSaleItemDto> Items { get; set; } = new();

    public decimal Discount { get; set; } = 0;
    public decimal Tax { get; set; } = 0;

    [Required]
    public string PaymentMethod { get; set; } = "cash";

    [Required]
    public long CashierId { get; set; }
}
