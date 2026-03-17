namespace RetailPOS.API.DTOs.Roles;

public class RoleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new List<string>();
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
