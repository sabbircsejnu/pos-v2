using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Auth;

public class RefreshTokenRequestDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
