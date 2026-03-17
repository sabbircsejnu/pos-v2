using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Users;

public class UpdateUserDto
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role ID is required")]
    public long RoleId { get; set; }

    public long? OutletId { get; set; }

    public bool IsActive { get; set; }
}
