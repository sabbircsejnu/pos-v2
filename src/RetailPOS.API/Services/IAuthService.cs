using RetailPOS.API.DTOs.Auth;

namespace RetailPOS.API.Services;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request);
    Task<bool> LogoutAsync(long userId);
    Task<UserInfoDto?> GetCurrentUserAsync(long userId);
}
