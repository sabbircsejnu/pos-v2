namespace RetailPOS.API.DTOs.Auth;

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserInfoDto User { get; set; } = null!;
}

public class UserInfoDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? RoleName { get; set; }
    public List<string> Permissions { get; set; } = new();
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }

    // Session role-switch state
    public string? RealRoleName { get; set; }
    public string? ActingRoleName { get; set; }
    public long? ActingOutletId { get; set; }
    public string? ActingOutletName { get; set; }
    public bool IsRoleSwitched { get; set; }
    public bool IsBusinessOwner { get; set; }
}
