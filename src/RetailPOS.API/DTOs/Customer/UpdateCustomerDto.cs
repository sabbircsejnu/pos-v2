using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Customer;

public class UpdateCustomerDto
{
    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Email { get; set; }
}
