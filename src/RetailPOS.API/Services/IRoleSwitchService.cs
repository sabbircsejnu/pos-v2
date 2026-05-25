using RetailPOS.API.DTOs.Auth;

namespace RetailPOS.API.Services;

public interface IRoleSwitchService
{
    /// <summary>Issue a new token where the user acts as <paramref name="request"/>.ActingRole at ActingOutletId.</summary>
    Task<LoginResponseDto> SwitchRoleAsync(long realUserId, RoleSwitchRequestDto request);

    /// <summary>Issue a fresh token returning to the user's real (BusinessOwner) role.</summary>
    Task<LoginResponseDto> ReturnOwnerAsync(long realUserId);
}
