using RetailPOS.API.DTOs.Roles;

namespace RetailPOS.API.Services;

public interface IRoleService
{
    Task<RoleDto> GetRoleByIdAsync(long id);
    Task<IEnumerable<RoleDto>> GetAllRolesAsync();
    Task<RoleDto> CreateRoleAsync(CreateRoleDto dto);
    Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleDto dto);
    Task<bool> DeleteRoleAsync(long id);
    Task<List<string>> GetAllPermissionsAsync();
}
