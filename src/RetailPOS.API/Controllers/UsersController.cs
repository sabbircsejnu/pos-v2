using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Application.Identity.Queries;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.API.DTOs.Users;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "users.view")]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ISender sender,
        ITenantAccessService tenantAccess,
        ILogger<UsersController> logger)
    {
        _sender = sender;
        _tenantAccess = tenantAccess;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated list of users with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<UserListDto>> GetUsers(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] long? roleId = null,
        [FromQuery] long? outletId = null,
        [FromQuery] bool? isActive = null)
    {
        try
        {
            long? businessId = _tenantAccess.IsSuperAdmin
                ? null
                : _tenantAccess.RequireBusinessId();
            var result = await _sender.Send(
                new SearchUsersQuery(
                    pageNumber,
                    pageSize,
                    search,
                    roleId,
                    outletId,
                    isActive,
                    businessId),
                HttpContext.RequestAborted);
            return Ok(ToDto(result));
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users");
            return StatusCode(500, new { error = "An error occurred while retrieving users" });
        }
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(long id, CancellationToken cancellationToken)
    {
        try
        {
            long? businessId = _tenantAccess.IsSuperAdmin
                ? null
                : _tenantAccess.RequireBusinessId();
            var user = await _sender.Send(
                new GetUserByIdQuery(id, businessId),
                cancellationToken);
            return Ok(ToDto(user));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the user" });
        }
    }

    /// <summary>
    /// Create new user
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "users.create")]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _sender.Send(new CreateUserCommand(ToProfile(dto), dto.Password), cancellationToken);
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, ToDto(user));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { error = "An error occurred while creating the user" });
        }
    }

    /// <summary>
    /// Update existing user
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "users.edit")]
    public async Task<ActionResult<UserDto>> UpdateUser(
        long id,
        [FromBody] UpdateUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _sender.Send(new UpdateUserCommand(id, ToProfile(dto)), cancellationToken);
            return Ok(ToDto(user));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the user" });
        }
    }

    /// <summary>
    /// Delete user (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "users.delete")]
    public async Task<ActionResult> DeleteUser(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteUserCommand(id), cancellationToken);
            return Ok(new { message = "User deleted successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the user" });
        }
    }

    /// <summary>
    /// Activate user
    /// </summary>
    [HttpPut("{id}/activate")]
    [Authorize(Policy = "users.edit")]
    public async Task<ActionResult> ActivateUser(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new SetUserActiveCommand(id, true), cancellationToken);
            return Ok(new { message = "User activated successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while activating the user" });
        }
    }

    /// <summary>
    /// Deactivate user
    /// </summary>
    [HttpPut("{id}/deactivate")]
    [Authorize(Policy = "users.edit")]
    public async Task<ActionResult> DeactivateUser(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new SetUserActiveCommand(id, false), cancellationToken);
            return Ok(new { message = "User deactivated successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while deactivating the user" });
        }
    }

    /// <summary>
    /// Change user password
    /// </summary>
    [HttpPost("{id}/change-password")]
    [Authorize(Policy = "users.edit")]
    public async Task<ActionResult> ChangePassword(
        long id,
        [FromBody] ChangePasswordDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(
                new ChangeUserPasswordCommand(
                    id,
                    dto.CurrentPassword,
                    dto.NewPassword,
                    dto.ConfirmPassword),
                cancellationToken);
            return Ok(new { message = "Password changed successfully" });
        }
        catch (ProtectedUserPasswordChangeException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {UserId}", id);
            return StatusCode(500, new { error = "An error occurred while changing the password" });
        }
    }

    /// <summary>
    /// Get users by outlet
    /// </summary>
    [HttpGet("outlet/{outletId}")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersByOutlet(
        long outletId,
        CancellationToken cancellationToken)
    {
        try
        {
            long? businessId = _tenantAccess.IsSuperAdmin
                ? null
                : _tenantAccess.RequireBusinessId();
            var users = await _sender.Send(
                new GetUsersByOutletQuery(outletId, businessId),
                cancellationToken);
            return Ok(users.Select(ToDto).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users for outlet {OutletId}", outletId);
            return StatusCode(500, new { error = "An error occurred while retrieving users" });
        }
    }

    /// <summary>
    /// Get users by role
    /// </summary>
    [HttpGet("role/{roleId}")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersByRole(
        long roleId,
        CancellationToken cancellationToken)
    {
        try
        {
            long? businessId = _tenantAccess.IsSuperAdmin
                ? null
                : _tenantAccess.RequireBusinessId();
            var users = await _sender.Send(
                new GetUsersByRoleQuery(roleId, businessId),
                cancellationToken);
            return Ok(users.Select(ToDto).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users for role {RoleId}", roleId);
            return StatusCode(500, new { error = "An error occurred while retrieving users" });
        }
    }

    /// <summary>
    /// Self-service password change for current logged-in user.
    /// No special permission required beyond being authenticated.
    /// User must provide current password for verification.
    /// </summary>
    [HttpPost("my-password")]
    [Authorize]
    public async Task<ActionResult> ChangeOwnPassword(
        [FromBody] ChangePasswordDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get current user ID from claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { error = "Unable to determine current user" });
            }

            await _sender.Send(
                new ChangeMyPasswordCommand(
                    dto.CurrentPassword,
                    dto.NewPassword,
                    dto.ConfirmPassword),
                cancellationToken);
            return Ok(new { message = "Your password has been changed successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for current user");
            return StatusCode(500, new { error = "An error occurred while changing your password" });
        }
    }

    private static UserGovernanceProfile ToProfile(CreateUserDto dto) => new(
        dto.Name,
        dto.Email,
        dto.RoleId,
        dto.OutletId,
        dto.WarehouseIds?.ToArray(),
        dto.OutletIds?.ToArray(),
        dto.DefaultLocationType,
        dto.DefaultLocationId,
        dto.InventoryLocationAccessScope,
        dto.BusinessId,
        dto.UserPermissions?.Cast<string?>().ToArray(),
        dto.IsActive);

    private static UserGovernanceProfile ToProfile(UpdateUserDto dto) => new(
        dto.Name,
        dto.Email,
        dto.RoleId,
        dto.OutletId,
        dto.WarehouseIds?.ToArray(),
        dto.OutletIds?.ToArray(),
        dto.DefaultLocationType,
        dto.DefaultLocationId,
        dto.InventoryLocationAccessScope,
        dto.BusinessId,
        dto.UserPermissions?.Cast<string?>().ToArray(),
        dto.IsActive);

    private static UserListDto ToDto(UserListReadModel users) => new()
    {
        Users = users.Users.Select(ToDto).ToArray(),
        TotalCount = users.TotalCount,
        PageNumber = users.PageNumber,
        PageSize = users.PageSize
    };

    private static UserDto ToDto(UserReadModel user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        RoleId = user.RoleId,
        RoleName = user.RoleName,
        OutletId = user.OutletId,
        OutletName = user.OutletName,
        BusinessId = user.BusinessId,
        BusinessName = user.BusinessName,
        InventoryLocationAccessScope = user.InventoryLocationAccessScope,
        DefaultLocationType = user.DefaultLocationType,
        DefaultLocationId = user.DefaultLocationId,
        UserPermissions = user.UserPermissions,
        OutletAssignments = user.OutletAssignments.Select(assignment => new UserOutletAssignmentDto
        {
            Id = assignment.Id,
            OutletId = assignment.OutletId,
            OutletName = assignment.OutletName,
            IsPrimary = assignment.IsPrimary,
            IsActive = assignment.IsActive
        }).ToArray(),
        WarehouseAssignments = user.WarehouseAssignments.Select(assignment => new UserWarehouseAssignmentDto
        {
            Id = assignment.Id,
            WarehouseId = assignment.WarehouseId,
            WarehouseName = assignment.WarehouseName,
            IsPrimary = assignment.IsPrimary,
            IsActive = assignment.IsActive
        }).ToArray(),
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
