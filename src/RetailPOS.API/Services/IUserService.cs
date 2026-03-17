using RetailPOS.API.DTOs.Users;

namespace RetailPOS.API.Services;

public interface IUserService
{
    Task<UserDto> GetUserByIdAsync(long id);
    Task<UserListDto> GetUsersAsync(int pageNumber = 1, int pageSize = 10, string? searchQuery = null, long? roleId = null, long? outletId = null, bool? isActive = null);
    Task<UserDto> CreateUserAsync(CreateUserDto dto);
    Task<UserDto> UpdateUserAsync(long id, UpdateUserDto dto);
    Task<bool> DeleteUserAsync(long id);
    Task<bool> ActivateUserAsync(long id);
    Task<bool> DeactivateUserAsync(long id);
    Task<bool> ChangePasswordAsync(long id, ChangePasswordDto dto);
    Task<IEnumerable<UserDto>> GetUsersByOutletAsync(long outletId);
    Task<IEnumerable<UserDto>> GetUsersByRoleAsync(long roleId);
}
