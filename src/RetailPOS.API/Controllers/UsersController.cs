using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Users;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "users.view")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
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
            var result = await _userService.GetUsersAsync(pageNumber, pageSize, search, roleId, outletId, isActive);
            return Ok(result);
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
    public async Task<ActionResult<UserDto>> GetUser(long id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
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
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserDto dto)
    {
        try
        {
            var user = await _userService.CreateUserAsync(dto);
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
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
    public async Task<ActionResult<UserDto>> UpdateUser(long id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var user = await _userService.UpdateUserAsync(id, dto);
            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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
    public async Task<ActionResult> DeleteUser(long id)
    {
        try
        {
            var result = await _userService.DeleteUserAsync(id);
            return Ok(new { message = "User deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
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
    public async Task<ActionResult> ActivateUser(long id)
    {
        try
        {
            await _userService.ActivateUserAsync(id);
            return Ok(new { message = "User activated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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
    public async Task<ActionResult> DeactivateUser(long id)
    {
        try
        {
            await _userService.DeactivateUserAsync(id);
            return Ok(new { message = "User deactivated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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
    public async Task<ActionResult> ChangePassword(long id, [FromBody] ChangePasswordDto dto)
    {
        try
        {
            await _userService.ChangePasswordAsync(id, dto);
            return Ok(new { message = "Password changed successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
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
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersByOutlet(long outletId)
    {
        try
        {
            var users = await _userService.GetUsersByOutletAsync(outletId);
            return Ok(users);
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
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersByRole(long roleId)
    {
        try
        {
            var users = await _userService.GetUsersByRoleAsync(roleId);
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users for role {RoleId}", roleId);
            return StatusCode(500, new { error = "An error occurred while retrieving users" });
        }
    }
}
