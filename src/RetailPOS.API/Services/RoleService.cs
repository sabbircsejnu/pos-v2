using System.Text.Json;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<RoleService> _logger;

    // All available permissions in the system
    private static readonly List<string> AllPermissions = new()
    {
        // User management
        "users.view", "users.create", "users.edit", "users.delete",
        
        // Role management
        "roles.view", "roles.create", "roles.edit", "roles.delete",
        
        // Product management
        "products.view", "products.create", "products.edit", "products.delete",
        
        // Inventory management
        "inventory.view", "inventory.adjust", "inventory.transfer",
        
        // Sales
        "sales.view", "sales.create", "sales.void", "sales.refund",
        
        // Purchase orders
        "purchases.view", "purchases.create", "purchases.edit", "purchases.approve",
        
        // GRN
        "grn.view", "grn.create", "grn.receive",
        
        // Customers
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        
        // Suppliers
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        
        // Reports
        "reports.sales", "reports.inventory", "reports.financial", "reports.export",
        
        // Settings
        "settings.view", "settings.edit",
        
        // Audit logs
        "audit.view",
        
        // Outlets & Warehouses
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete"
    };

    public RoleService(IRoleRepository roleRepository, ILogger<RoleService> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<RoleDto> GetRoleByIdAsync(long id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        
        if (role == null)
        {
            throw new KeyNotFoundException($"Role with ID {id} not found");
        }

        return MapToDto(role);
    }

    public async Task<IEnumerable<RoleDto>> GetAllRolesAsync()
    {
        var roles = await _roleRepository.GetAllAsync();
        return roles.Select(MapToDto);
    }

    public async Task<RoleDto> CreateRoleAsync(CreateRoleDto dto)
    {
        // Check if role name already exists
        if (await _roleRepository.ExistsAsync(dto.Name))
        {
            throw new InvalidOperationException($"Role with name '{dto.Name}' already exists");
        }

        // Validate permissions
        var invalidPermissions = dto.Permissions
            .Where(p => p != "*" && !AllPermissions.Contains(p))
            .ToList();

        if (invalidPermissions.Any())
        {
            throw new InvalidOperationException($"Invalid permissions: {string.Join(", ", invalidPermissions)}");
        }

        var role = new Role
        {
            Name = dto.Name,
            Permissions = JsonSerializer.Serialize(dto.Permissions)
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

        // Check if name is being changed to an existing name
        if (role.Name != dto.Name && await _roleRepository.ExistsAsync(dto.Name))
        {
            throw new InvalidOperationException($"Role with name '{dto.Name}' already exists");
        }

        // Validate permissions
        var invalidPermissions = dto.Permissions
            .Where(p => p != "*" && !AllPermissions.Contains(p))
            .ToList();

        if (invalidPermissions.Any())
        {
            throw new InvalidOperationException($"Invalid permissions: {string.Join(", ", invalidPermissions)}");
        }

        role.Name = dto.Name;
        role.Permissions = JsonSerializer.Serialize(dto.Permissions);

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
        return await Task.FromResult(AllPermissions);
    }

    private RoleDto MapToDto(Role role)
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

        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Permissions = permissions,
            UserCount = role.Users?.Count ?? 0,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }
}
