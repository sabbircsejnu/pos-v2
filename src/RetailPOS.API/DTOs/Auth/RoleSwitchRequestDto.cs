namespace RetailPOS.API.DTOs.Auth;

public class RoleSwitchRequestDto
{
    public string ActingRole { get; set; } = string.Empty;
    public long? ActingOutletId { get; set; }
}
