using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Roles;

public class UpdateRoleDto
{
    [Required(ErrorMessage = "Role name is required")]
    [StringLength(50, ErrorMessage = "Role name cannot exceed 50 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Permissions are required")]
    public List<string> Permissions { get; set; } = new List<string>();
}
