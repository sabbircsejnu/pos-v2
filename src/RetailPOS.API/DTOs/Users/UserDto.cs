namespace RetailPOS.API.DTOs.Users;

public class UserDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public string? RoleName { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
