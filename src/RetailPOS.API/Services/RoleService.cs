using System.Text.Json;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class RoleService : IRoleService
{
    private static readonly Dictionary<string, string[]> LegacyPermissionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inventory.adjust"] = new[]
        {
            "stock_adjustments.view",
            "stock_adjustments.create",
            "stock_adjustments.edit",
            "stock_adjustments.delete",
            "stock_adjustments.approve",
            "stock_adjustments.reject",
            "stock_adjustments.cancel"
        },
        ["inventory.transfer"] = new[]
        {
            "stock_transfers.view",
            "stock_transfers.create",
            "stock_transfers.edit",
            "stock_transfers.delete",
            "stock_transfers.approve",
            "stock_transfers.cancel",
            "stock_transfers.dispatch",
            "stock_transfers.receive",
            "stock_transfers.reject_receive",
            "stock_transfers.return_create",
            "stock_transfers.transfer_from_any_location",
            "stock_requisitions.view",
            "stock_requisitions.create",
            "stock_requisitions.edit",
            "stock_requisitions.approve",
            "stock_requisitions.reject",
            "stock_requisitions.convert_to_transfer"
        }
    };

    private readonly IRoleRepository _roleRepository;
    private readonly IRoleSwitchContext _roleSwitchContext;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        IRoleRepository roleRepository,
        IRoleSwitchContext roleSwitchContext,
        ITenantAccessService tenantAccess,
        ILogger<RoleService> logger)
    {
        _roleRepository = roleRepository;
        _roleSwitchContext = roleSwitchContext;
        _tenantAccess = tenantAccess;
        _logger = logger;
    }

    public async Task<RoleDto> GetRoleByIdAsync(long id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        
        if (role == null || (IsSuperAdminRole(role.Name) && !_roleSwitchContext.IsSuperAdmin))
        {
            throw new KeyNotFoundException($"Role with ID {id} not found");
        }

        return MapToDto(role);
    }

    public async Task<IEnumerable<RoleDto>> GetAllRolesAsync()
    {
        var roles = await _roleRepository.GetAllAsync();

        if (!_roleSwitchContext.IsSuperAdmin)
        {
            roles = roles.Where(r => !IsSuperAdminRole(r.Name));
        }

        // Scope user count to caller's business so it matches the modal's tenant-scoped query
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.EffectiveBusinessId;
        return roles.Select(r => MapToDto(r, businessId));
    }

    public async Task<RoleDto> CreateRoleAsync(CreateRoleDto dto)
    {
        if (IsSuperAdminRole(dto.Name) && !_roleSwitchContext.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        // Check if role name already exists
        if (await _roleRepository.ExistsAsync(dto.Name))
        {
            throw new InvalidOperationException($"Role with name '{dto.Name}' already exists");
        }

        var normalizedPermissions = NormalizePermissions(dto.Permissions);

        // Validate permissions
        var invalidPermissions = normalizedPermissions
            .Where(p => p != "*" && !PermissionCatalog.All.Contains(p))
            .ToList();

        if (invalidPermissions.Any())
        {
            throw new InvalidOperationException($"Invalid permissions: {string.Join(", ", invalidPermissions)}");
        }

        var role = new Role
        {
            Name = dto.Name,
            Permissions = JsonSerializer.Serialize(normalizedPermissions)
        };

        var createdRole = await _roleRepository.CreateAsync(role);
        _logger.LogInformation("Role created: {RoleName}", dto.Name);

        return MapToDto(createdRole);
    }

    public async Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleDto dto)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        
        if (role == null)
        {
            throw new KeyNotFoundException($"Role with ID {id} not found");
        }

        if (IsSuperAdminRole(role.Name) && !_roleSwitchContext.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        if (IsSuperAdminRole(dto.Name) && !_roleSwitchContext.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        // Check if name is being changed to an existing name
        if (role.Name != dto.Name && await _roleRepository.ExistsAsync(dto.Name))
        {
            throw new InvalidOperationException($"Role with name '{dto.Name}' already exists");
        }

        var normalizedPermissions = NormalizePermissions(dto.Permissions);

        // Validate permissions
        var invalidPermissions = normalizedPermissions
            .Where(p => p != "*" && !PermissionCatalog.All.Contains(p))
            .ToList();

        if (invalidPermissions.Any())
        {
            throw new InvalidOperationException($"Invalid permissions: {string.Join(", ", invalidPermissions)}");
        }

        role.Name = dto.Name;
        role.Permissions = JsonSerializer.Serialize(normalizedPermissions);

        var updatedRole = await _roleRepository.UpdateAsync(role);
        _logger.LogInformation("Role updated: {RoleName}", dto.Name);

        return MapToDto(updatedRole);
    }

    public async Task<bool> DeleteRoleAsync(long id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        
        if (role == null)
        {
            throw new KeyNotFoundException($"Role with ID {id} not found");
        }

        if (IsSuperAdminRole(role.Name))
        {
            throw new InvalidOperationException("Super Admin role cannot be deleted.");
        }

        // Check if role is in use
        if (await _roleRepository.IsRoleInUseAsync(id))
        {
            throw new InvalidOperationException($"Cannot delete role '{role.Name}' because it is assigned to users");
        }

        var result = await _roleRepository.DeleteAsync(id);
        
        if (result)
        {
            _logger.LogInformation("Role deleted: {RoleName}", role.Name);
        }

        return result;
    }

    public async Task<List<string>> GetAllPermissionsAsync()
    {
        return await Task.FromResult(PermissionCatalog.All.ToList());
    }

    private static bool IsSuperAdminRole(string? roleName) =>
        string.Equals(roleName, RoleSwitchClaims.SuperAdminRoleName, StringComparison.OrdinalIgnoreCase);

    private static List<string> NormalizePermissions(IEnumerable<string>? permissions)
    {
        if (permissions == null)
        {
            return new List<string>();
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            var key = permission.Trim();

            if (key == "*")
            {
                return new List<string> { "*" };
            }

            if (LegacyPermissionAliases.TryGetValue(key, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    normalized.Add(alias);
                }
                continue;
            }

            normalized.Add(key);
        }

        return normalized.ToList();
    }

    private RoleDto MapToDto(Role role, long? businessId = null)
    {
        List<string> permissions;
        try
        {
            permissions = JsonSerializer.Deserialize<List<string>>(role.Permissions) ?? new List<string>();
        }
        catch
        {
            permissions = new List<string>();
        }

        var userCount = businessId.HasValue
            ? role.Users?.Count(u => u.BusinessId == businessId) ?? 0
            : role.Users?.Count ?? 0;

        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Permissions = permissions,
            UserCount = userCount,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }
}
