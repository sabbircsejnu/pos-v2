using RetailPOS.API.DTOs.Users;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Infrastructure.Data;
using BCrypt.Net;

namespace RetailPOS.API.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IOutletRepository _outletRepository;
    private readonly ITenantAccessService _tenantAccess;
    private readonly IRoleAssignmentPolicyService _roleAssignmentPolicy;
    private readonly RetailPOSDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IOutletRepository outletRepository,
        ITenantAccessService tenantAccess,
        IRoleAssignmentPolicyService roleAssignmentPolicy,
        RetailPOSDbContext db,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _outletRepository = outletRepository;
        _tenantAccess = tenantAccess;
        _roleAssignmentPolicy = roleAssignmentPolicy;
        _db = db;
        _logger = logger;
    }

    public async Task<UserDto> GetUserByIdAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        return await MapToDtoAsync(user);
    }

    public async Task<UserListDto> GetUsersAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? searchQuery = null,
        long? roleId = null,
        long? outletId = null,
        bool? isActive = null)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var users = await _userRepository.GetAllAsync(pageNumber, pageSize, searchQuery, roleId, outletId, isActive, businessId);
        var totalCount = await _userRepository.GetTotalCountAsync(searchQuery, roleId, outletId, isActive, businessId);

        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            userDtos.Add(await MapToDtoAsync(user));
        }

        return new UserListDto
        {
            Users = userDtos,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<UserDto> CreateUserAsync(CreateUserDto dto)
    {
        // Check if email already exists
        if (await _userRepository.ExistsAsync(dto.Email))
        {
            throw new InvalidOperationException($"User with email '{dto.Email}' already exists");
        }

        // Validate role exists
        var role = await _roleRepository.GetByIdAsync(dto.RoleId);
        if (role == null)
        {
            throw new KeyNotFoundException($"Role with ID {dto.RoleId} not found");
        }

        var accessScope = NormalizeAccessScope(dto.InventoryLocationAccessScope);

        // Determine business ID
        long? businessId = _tenantAccess.IsSuperAdmin ? dto.BusinessId : _tenantAccess.RequireBusinessId();
        
        if (_tenantAccess.IsSuperAdmin && !dto.BusinessId.HasValue)
        {
            throw new InvalidOperationException("SuperAdmin must specify BusinessId when creating tenant users.");
        }

        // Validate outlet if provided
        if (dto.OutletId.HasValue)
        {
            var outlet = await _outletRepository.GetByIdAsync(dto.OutletId.Value, businessId);
            if (outlet == null)
                throw new UnauthorizedAccessException($"Outlet #{dto.OutletId.Value} is not in your authorized business scope.");

            businessId ??= outlet.BusinessId;
        }

        var outletList = (dto.OutletIds ?? Enumerable.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        // Validate outlet assignment list if provided
        if (outletList.Count > 0)
        {
            var scopedOutlets = await _db.Outlets
                .Where(o => outletList.Contains(o.Id) && o.BusinessId == businessId)
                .ToListAsync();

            if (scopedOutlets.Count != outletList.Count)
            {
                throw new UnauthorizedAccessException("One or more outlets are not in your authorized business scope.");
            }
        }

        // Validate warehouse IDs if provided
        var warehouseList = (dto.WarehouseIds ?? Enumerable.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (warehouseList.Count > 0)
        {
            var warehouses = await _db.Warehouses
                .Where(w => warehouseList.Contains(w.Id) && w.BusinessId == businessId)
                .ToListAsync();

            if (warehouses.Count != warehouseList.Count)
            {
                throw new UnauthorizedAccessException("One or more warehouses are not in your authorized business scope.");
            }
        }

        var (defaultLocationType, defaultLocationId) = ResolveDefaultLocation(
            dto.DefaultLocationType,
            dto.DefaultLocationId,
            dto.OutletId,
            outletList,
            warehouseList);

        var effectiveDefaultOutletId = defaultLocationType == "outlet" ? defaultLocationId : null;

        ApplyAllLocationsAssignmentPolicy(accessScope, role.Name, outletList, warehouseList);

        if (accessScope != User.InventoryAccessAll && defaultLocationType == "outlet" && defaultLocationId.HasValue && !outletList.Contains(defaultLocationId.Value))
        {
            outletList.Insert(0, defaultLocationId.Value);
        }

        if (accessScope != User.InventoryAccessAll && defaultLocationType == "warehouse" && defaultLocationId.HasValue && !warehouseList.Contains(defaultLocationId.Value))
        {
            warehouseList.Insert(0, defaultLocationId.Value);
        }

        await ValidateDefaultLocationScopeAsync(businessId, defaultLocationType, defaultLocationId);

        var outletValidationError = _roleAssignmentPolicy.ValidateOutletAssignment(role.Name, effectiveDefaultOutletId, warehouseList);
        if (!string.IsNullOrWhiteSpace(outletValidationError))
        {
            throw new InvalidOperationException(outletValidationError);
        }

        var warehouseValidationError = _roleAssignmentPolicy.ValidateWarehouseAssignment(role.Name, warehouseList);
        if (!string.IsNullOrWhiteSpace(warehouseValidationError))
        {
            throw new InvalidOperationException(warehouseValidationError);
        }

        // Check business user limits
        if (businessId.HasValue)
        {
            var businessLimits = await _db.Businesses
                .AsNoTracking()
                .Where(b => b.Id == businessId.Value)
                .Select(b => new { b.MaxUsers })
                .FirstOrDefaultAsync();

            if (businessLimits != null && businessLimits.MaxUsers.HasValue)
            {
                var existingUsers = await _db.Users.LongCountAsync(u => u.BusinessId == businessId.Value && u.IsActive);
                if (existingUsers >= businessLimits.MaxUsers.Value)
                    throw new InvalidOperationException(
                        $"User limit reached for this business ({businessLimits.MaxUsers.Value}). Upgrade subscription or increase user limit.");
            }
        }

        // Hash password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 11);

        ValidateInventoryAccessConfiguration(accessScope, effectiveDefaultOutletId, outletList, warehouseList);

        var user = new User
        {
            BusinessId = businessId,
            Name = dto.Name,
            Email = dto.Email,
            PasswordHash = passwordHash,
            RoleId = dto.RoleId,
            OutletId = defaultLocationType == "outlet" ? defaultLocationId : null,
            InventoryLocationAccessScope = accessScope,
            IsActive = dto.IsActive
        };

        var createdUser = await _userRepository.CreateAsync(user);

        if (outletList.Count > 0)
        {
            for (int i = 0; i < outletList.Count; i++)
            {
                var assignment = new UserOutletAssignment
                {
                    UserId = createdUser.Id,
                    OutletId = outletList[i],
                    BusinessId = businessId!.Value,
                    IsPrimary = (i == 0),
                    IsActive = true
                };
                _db.Add(assignment);
            }
        }

        if (warehouseList.Count > 0)
        {
            for (int i = 0; i < warehouseList.Count; i++)
            {
                var assignment = new UserWarehouseAssignment
                {
                    UserId = createdUser.Id,
                    WarehouseId = warehouseList[i],
                    BusinessId = businessId!.Value,
                    IsPrimary = (i == 0), // First warehouse is primary
                    IsActive = true
                };
                _db.Add(assignment);
            }
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("User created: {Email} with {WarehouseCount} warehouse assignments", dto.Email, warehouseList.Count);

        return await MapToDtoAsync(createdUser);
    }

    public async Task<UserDto> UpdateUserAsync(long id, UpdateUserDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        // Check if email is being changed to an existing email
        if (user.Email != dto.Email && await _userRepository.ExistsAsync(dto.Email))
        {
            throw new InvalidOperationException($"User with email '{dto.Email}' already exists");
        }

        // Validate role exists
        var role = await _roleRepository.GetByIdAsync(dto.RoleId);
        if (role == null)
        {
            throw new KeyNotFoundException($"Role with ID {dto.RoleId} not found");
        }

        var accessScope = NormalizeAccessScope(dto.InventoryLocationAccessScope);

        var outletList = (dto.OutletIds ?? Enumerable.Empty<long>())
            .Where(outletId => outletId > 0)
            .Distinct()
            .ToList();

        var warehouseList = (dto.WarehouseIds ?? Enumerable.Empty<long>())
            .Where(warehouseId => warehouseId > 0)
            .Distinct()
            .ToList();

        var (defaultLocationType, defaultLocationId) = ResolveDefaultLocation(
            dto.DefaultLocationType,
            dto.DefaultLocationId,
            dto.OutletId,
            outletList,
            warehouseList);

        var targetBusinessId = await ResolveUpdateBusinessIdAsync(
            user,
            dto,
            role.Name,
            defaultLocationType,
            defaultLocationId,
            outletList,
            warehouseList);

        // Validate outlet if provided
        if (dto.OutletId.HasValue)
        {
            var scopedOutlet = await _outletRepository.GetByIdAsync(dto.OutletId.Value, targetBusinessId);
            if (scopedOutlet == null)
                throw new UnauthorizedAccessException($"Outlet #{dto.OutletId.Value} is not in the user's business scope.");
        }

        if (outletList.Count > 0)
        {
            var outlets = await _db.Outlets
                .Where(o => outletList.Contains(o.Id) && o.BusinessId == targetBusinessId)
                .ToListAsync();

            if (outlets.Count != outletList.Count)
            {
                throw new UnauthorizedAccessException("One or more outlets are not in the user's business scope.");
            }
        }

        if (warehouseList.Count > 0)
        {
            var warehouses = await _db.Warehouses
                .Where(w => warehouseList.Contains(w.Id) && w.BusinessId == targetBusinessId)
                .ToListAsync();

            if (warehouses.Count != warehouseList.Count)
            {
                throw new UnauthorizedAccessException("One or more warehouses are not in the user's business scope.");
            }
        }

        var effectiveDefaultOutletId = defaultLocationType == "outlet" ? defaultLocationId : null;

        ApplyAllLocationsAssignmentPolicy(accessScope, role.Name, outletList, warehouseList);

        if (accessScope != User.InventoryAccessAll && defaultLocationType == "outlet" && defaultLocationId.HasValue && !outletList.Contains(defaultLocationId.Value))
        {
            outletList.Insert(0, defaultLocationId.Value);
        }

        if (accessScope != User.InventoryAccessAll && defaultLocationType == "warehouse" && defaultLocationId.HasValue && !warehouseList.Contains(defaultLocationId.Value))
        {
            warehouseList.Insert(0, defaultLocationId.Value);
        }

        await ValidateDefaultLocationScopeAsync(targetBusinessId, defaultLocationType, defaultLocationId);

        var outletValidationError = _roleAssignmentPolicy.ValidateOutletAssignment(role.Name, effectiveDefaultOutletId, warehouseList);
        if (!string.IsNullOrWhiteSpace(outletValidationError))
        {
            throw new InvalidOperationException(outletValidationError);
        }

        var warehouseValidationError = _roleAssignmentPolicy.ValidateWarehouseAssignment(role.Name, warehouseList);
        if (!string.IsNullOrWhiteSpace(warehouseValidationError))
        {
            throw new InvalidOperationException(warehouseValidationError);
        }

        ValidateInventoryAccessConfiguration(accessScope, effectiveDefaultOutletId, outletList, warehouseList);

        user.BusinessId = targetBusinessId;
        user.Name = dto.Name;
        user.Email = dto.Email;
        user.RoleId = dto.RoleId;
        user.OutletId = defaultLocationType == "outlet" ? defaultLocationId : null;
        user.InventoryLocationAccessScope = accessScope;
        user.IsActive = dto.IsActive;

        var updatedUser = await _userRepository.UpdateAsync(user);

        // Update outlet assignments
        var existingOutletAssignments = await _db.UserOutletAssignments
            .Where(a => a.UserId == id)
            .ToListAsync();

        _db.RemoveRange(existingOutletAssignments);

        if (outletList.Count > 0)
        {
            if (!user.BusinessId.HasValue)
            {
                throw new InvalidOperationException("BusinessId is required before saving outlet assignments.");
            }

            for (int i = 0; i < outletList.Count; i++)
            {
                var assignment = new UserOutletAssignment
                {
                    UserId = id,
                    OutletId = outletList[i],
                    BusinessId = user.BusinessId.Value,
                    IsPrimary = (i == 0),
                    IsActive = true
                };
                _db.Add(assignment);
            }
        }

        // Update warehouse assignments
        var existingAssignments = await _db.UserWarehouseAssignments
            .Where(a => a.UserId == id)
            .ToListAsync();

        _db.RemoveRange(existingAssignments);

        if (warehouseList.Count > 0)
        {
            if (!user.BusinessId.HasValue)
            {
                throw new InvalidOperationException("BusinessId is required before saving warehouse assignments.");
            }

            for (int i = 0; i < warehouseList.Count; i++)
            {
                var assignment = new UserWarehouseAssignment
                {
                    UserId = id,
                    WarehouseId = warehouseList[i],
                    BusinessId = user.BusinessId.Value,
                    IsPrimary = (i == 0), // First warehouse is primary
                    IsActive = true
                };
                _db.Add(assignment);
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("User updated: {Email} with {WarehouseCount} warehouse assignments", dto.Email, warehouseList.Count);

        return await MapToDtoAsync(updatedUser);
    }

    public async Task<bool> DeleteUserAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        var result = await _userRepository.DeleteAsync(id, businessId);
        
        if (result)
        {
            _logger.LogInformation("User deleted (soft): {Email}", user.Email);
        }

        return result;
    }

    public async Task<bool> ActivateUserAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        if (user.IsActive)
        {
            throw new InvalidOperationException("User is already active");
        }

        user.IsActive = true;
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User activated: {Email}", user.Email);

        return true;
    }

    public async Task<bool> DeactivateUserAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException("User is already inactive");
        }

        user.IsActive = false;
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User deactivated: {Email}", user.Email);

        return true;
    }

    public async Task<bool> ChangePasswordAsync(long id, ChangePasswordDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var user = await _userRepository.GetByIdAsync(id, businessId);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        // Verify current password
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Current password is incorrect");
        }

        // Hash new password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor: 11);
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("Password changed for user: {Email}", user.Email);

        return true;
    }

    public async Task<IEnumerable<UserDto>> GetUsersByOutletAsync(long outletId)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var users = await _userRepository.GetByOutletIdAsync(outletId, businessId);
        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            dtos.Add(await MapToDtoAsync(user));
        }
        return dtos;
    }

    public async Task<IEnumerable<UserDto>> GetUsersByRoleAsync(long roleId)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var users = await _userRepository.GetByRoleIdAsync(roleId, businessId);
        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            dtos.Add(await MapToDtoAsync(user));
        }
        return dtos;
    }

    private async Task<UserDto> MapToDtoAsync(User user)
    {
        var outletAssignments = await _db.UserOutletAssignments
            .Where(a => a.UserId == user.Id && a.IsActive)
            .Include(a => a.Outlet)
            .Select(a => new UserOutletAssignmentDto
            {
                Id = a.Id,
                OutletId = a.OutletId,
                OutletName = a.Outlet!.Name,
                IsPrimary = a.IsPrimary,
                IsActive = a.IsActive
            })
            .ToListAsync();

        // Get warehouse assignments if any
        var warehouseAssignments = await _db.UserWarehouseAssignments
            .Where(a => a.UserId == user.Id && a.IsActive)
            .Include(a => a.Warehouse)
            .Select(a => new UserWarehouseAssignmentDto
            {
                Id = a.Id,
                WarehouseId = a.WarehouseId,
                WarehouseName = a.Warehouse!.Name,
                IsPrimary = a.IsPrimary,
                IsActive = a.IsActive
            })
            .ToListAsync();

        var primaryWarehouse = warehouseAssignments.FirstOrDefault(w => w.IsPrimary) ?? warehouseAssignments.FirstOrDefault();

        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            RoleId = user.RoleId,
            RoleName = user.Role?.Name,
            OutletId = user.OutletId,
            OutletName = user.Outlet?.Name,
            BusinessId = user.BusinessId,
            BusinessName = user.Business?.Name,
            InventoryLocationAccessScope = string.IsNullOrWhiteSpace(user.InventoryLocationAccessScope)
                ? User.InventoryAccessAssignedOnly
                : user.InventoryLocationAccessScope,
            DefaultLocationType = user.OutletId.HasValue
                ? "outlet"
                : primaryWarehouse != null
                    ? "warehouse"
                    : null,
            DefaultLocationId = user.OutletId
                ?? primaryWarehouse?.WarehouseId,
            OutletAssignments = outletAssignments,
            WarehouseAssignments = warehouseAssignments,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    private static (string? LocationType, long? LocationId) ResolveDefaultLocation(
        string? defaultLocationType,
        long? defaultLocationId,
        long? legacyDefaultOutletId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        var normalizedType = NormalizeDefaultLocationType(defaultLocationType);

        if (defaultLocationId.HasValue)
        {
            if (normalizedType is null)
                throw new InvalidOperationException("DefaultLocationType is required when DefaultLocationId is provided.");

            return (normalizedType, defaultLocationId.Value);
        }

        if (legacyDefaultOutletId.HasValue)
            return ("outlet", legacyDefaultOutletId.Value);

        if (outletIds.Count > 0)
            return ("outlet", outletIds.First());

        if (warehouseIds.Count > 0)
            return ("warehouse", warehouseIds.First());

        return (null, null);
    }

    private static string? NormalizeDefaultLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            return null;

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException("DefaultLocationType must be either outlet or warehouse.");

        return normalized;
    }

    private async Task ValidateDefaultLocationScopeAsync(long? businessId, string? locationType, long? locationId)
    {
        if (!businessId.HasValue || !locationId.HasValue || string.IsNullOrWhiteSpace(locationType))
            return;

        if (locationType == "outlet")
        {
            var outletExists = await _db.Outlets.AnyAsync(o => o.Id == locationId.Value && o.BusinessId == businessId.Value);
            if (!outletExists)
                throw new UnauthorizedAccessException($"Default outlet #{locationId.Value} is not in the user's business scope.");
            return;
        }

        var warehouseExists = await _db.Warehouses.AnyAsync(w => w.Id == locationId.Value && w.BusinessId == businessId.Value);
        if (!warehouseExists)
            throw new UnauthorizedAccessException($"Default warehouse #{locationId.Value} is not in the user's business scope.");
    }

    private static string NormalizeAccessScope(string? scope)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            User.InventoryAccessAssignedOnly => User.InventoryAccessAssignedOnly,
            User.InventoryAccessSpecific => User.InventoryAccessSpecific,
            User.InventoryAccessAll => User.InventoryAccessAll,
            _ => User.InventoryAccessAssignedOnly
        };
    }

    private static void ValidateInventoryAccessConfiguration(
        string accessScope,
        long? defaultOutletId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (accessScope == User.InventoryAccessAssignedOnly)
        {
            if (!defaultOutletId.HasValue && warehouseIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Assigned Location Only requires a default outlet or at least one assigned warehouse.");
            }

            return;
        }

        if (accessScope == User.InventoryAccessSpecific)
        {
            if (outletIds.Count == 0 && warehouseIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Specific Outlets/Warehouses requires at least one assigned outlet or warehouse.");
            }

            return;
        }

        if (accessScope != User.InventoryAccessAll)
        {
            throw new InvalidOperationException($"Unsupported inventory access scope '{accessScope}'.");
        }
    }

    private void ApplyAllLocationsAssignmentPolicy(
        string accessScope,
        string? roleName,
        List<long> outletIds,
        List<long> warehouseIds)
    {
        if (accessScope != User.InventoryAccessAll)
            return;

        // For all_locations, explicit assignment collections are optional and can be omitted.
        outletIds.Clear();

        if (!_roleAssignmentPolicy.IsWarehouseRequired(roleName))
        {
            warehouseIds.Clear();
        }
    }

    private async Task<long?> ResolveUpdateBusinessIdAsync(
        User user,
        UpdateUserDto dto,
        string? roleName,
        string? defaultLocationType,
        long? defaultLocationId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (!_tenantAccess.IsSuperAdmin)
        {
            var scopedBusinessId = _tenantAccess.RequireBusinessId();
            if (user.BusinessId.HasValue && user.BusinessId.Value != scopedBusinessId)
            {
                throw new UnauthorizedAccessException("You are not authorized to update a user outside your business scope.");
            }

            return scopedBusinessId;
        }

        long? businessId = dto.BusinessId ?? user.BusinessId;

        if (!businessId.HasValue)
        {
            var inferredBusinessIds = new HashSet<long>();

            var candidateOutletIds = new HashSet<long>(outletIds);
            if (dto.OutletId.HasValue)
            {
                candidateOutletIds.Add(dto.OutletId.Value);
            }

            if (defaultLocationType == "outlet" && defaultLocationId.HasValue)
            {
                candidateOutletIds.Add(defaultLocationId.Value);
            }

            if (candidateOutletIds.Count > 0)
            {
                var outletBusinessIds = await _db.Outlets
                    .Where(o => candidateOutletIds.Contains(o.Id))
                    .Select(o => o.BusinessId)
                    .Distinct()
                    .ToListAsync();

                foreach (var outletBusinessId in outletBusinessIds)
                {
                    if (outletBusinessId.HasValue)
                    {
                        inferredBusinessIds.Add(outletBusinessId.Value);
                    }
                }
            }

            var candidateWarehouseIds = new HashSet<long>(warehouseIds);
            if (defaultLocationType == "warehouse" && defaultLocationId.HasValue)
            {
                candidateWarehouseIds.Add(defaultLocationId.Value);
            }

            if (candidateWarehouseIds.Count > 0)
            {
                var warehouseBusinessIds = await _db.Warehouses
                    .Where(w => candidateWarehouseIds.Contains(w.Id))
                    .Select(w => w.BusinessId)
                    .Distinct()
                    .ToListAsync();

                foreach (var warehouseBusinessId in warehouseBusinessIds)
                {
                    if (warehouseBusinessId.HasValue)
                    {
                        inferredBusinessIds.Add(warehouseBusinessId.Value);
                    }
                }
            }

            if (inferredBusinessIds.Count > 1)
            {
                throw new InvalidOperationException("Selected outlets/warehouses must belong to the same business.");
            }

            businessId = inferredBusinessIds.FirstOrDefault();
            if (businessId == 0)
            {
                businessId = null;
            }
        }

        bool roleRequiresBusiness =
            !string.Equals(roleName, RoleSwitchClaims.SuperAdminRoleName, StringComparison.OrdinalIgnoreCase);

        if (roleRequiresBusiness && !businessId.HasValue)
        {
            throw new InvalidOperationException(
                "BusinessId is required for SuperAdmin user updates. Provide BusinessId or select a default location inside the target business.");
        }

        if (businessId.HasValue)
        {
            var businessExists = await _db.Businesses.AnyAsync(b => b.Id == businessId.Value);
            if (!businessExists)
            {
                throw new KeyNotFoundException($"Business with ID {businessId.Value} not found");
            }
        }

        return businessId;
    }
}
