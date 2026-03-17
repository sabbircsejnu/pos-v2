using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Auth;

public class RegisterRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
