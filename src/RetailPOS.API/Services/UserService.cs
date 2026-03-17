using RetailPOS.API.DTOs.Users;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;
using BCrypt.Net;

namespace RetailPOS.API.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<UserDto> GetUserByIdAsync(long id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        return MapToDto(user);
    }

    public async Task<UserListDto> GetUsersAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? searchQuery = null,
        long? roleId = null,
        long? outletId = null,
        bool? isActive = null)
    {
        var users = await _userRepository.GetAllAsync(pageNumber, pageSize, searchQuery, roleId, outletId, isActive);
        var totalCount = await _userRepository.GetTotalCountAsync(searchQuery, roleId, outletId, isActive);

        return new UserListDto
        {
            Users = users.Select(MapToDto),
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

        // Hash password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 11);

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            PasswordHash = passwordHash,
            RoleId = dto.RoleId,
            OutletId = dto.OutletId,
            IsActive = dto.IsActive
        };

        var createdUser = await _userRepository.CreateAsync(user);
        _logger.LogInformation("User created: {Email} by system", dto.Email);

        return MapToDto(createdUser);
    }

    public async Task<UserDto> UpdateUserAsync(long id, UpdateUserDto dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        
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

        user.Name = dto.Name;
        user.Email = dto.Email;
        user.RoleId = dto.RoleId;
        user.OutletId = dto.OutletId;
        user.IsActive = dto.IsActive;

        var updatedUser = await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User updated: {Email}", dto.Email);

        return MapToDto(updatedUser);
    }

    public async Task<bool> DeleteUserAsync(long id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found");
        }

        var result = await _userRepository.DeleteAsync(id);
        
        if (result)
        {
            _logger.LogInformation("User deleted (soft): {Email}", user.Email);
        }

        return result;
    }

    public async Task<bool> ActivateUserAsync(long id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        
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
        var user = await _userRepository.GetByIdAsync(id);
        
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
        var user = await _userRepository.GetByIdAsync(id);
        
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
        var users = await _userRepository.GetByOutletIdAsync(outletId);
        return users.Select(MapToDto);
    }

    public async Task<IEnumerable<UserDto>> GetUsersByRoleAsync(long roleId)
    {
        var users = await _userRepository.GetByRoleIdAsync(roleId);
        return users.Select(MapToDto);
    }

    private UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            RoleId = user.RoleId,
            RoleName = user.Role?.Name,
            OutletId = user.OutletId,
            OutletName = user.Outlet?.Name,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
