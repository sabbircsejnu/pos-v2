using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class VoidSaleDto
{
    [Required]
    [MinLength(3, ErrorMessage = "Void reason must be at least 3 characters")]
    public string? Reason { get; set; }
}
