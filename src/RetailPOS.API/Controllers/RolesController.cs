using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "roles.view")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly ILogger<RolesController> _logger;

    public RolesController(IRoleService roleService, ILogger<RolesController> logger)
    {
        _roleService = roleService;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetRoles()
    {
        try
        {
            var roles = await _roleService.GetAllRolesAsync();
            return Ok(roles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving roles");
            return StatusCode(500, new { error = "An error occurred while retrieving roles" });
        }
    }

    /// <summary>
    /// Get role by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDto>> GetRole(long id)
    {
        try
        {
            var role = await _roleService.GetRoleByIdAsync(id);
            return Ok(role);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving role {RoleId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the role" });
        }
    }

    /// <summary>
    /// Create new role
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "roles.create")]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleDto dto)
    {
        try
        {
            var role = await _roleService.CreateRoleAsync(dto);
            return CreatedAtAction(nameof(GetRole), new { id = role.Id }, role);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating role");
            return StatusCode(500, new { error = "An error occurred while creating the role" });
        }
    }

    /// <summary>
    /// Update existing role
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "roles.edit")]
    public async Task<ActionResult<RoleDto>> UpdateRole(long id, [FromBody] UpdateRoleDto dto)
    {
        try
        {
            var role = await _roleService.UpdateRoleAsync(id, dto);
            return Ok(role);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
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
            _logger.LogError(ex, "Error updating role {RoleId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the role" });
        }
    }

    /// <summary>
    /// Delete role
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "roles.delete")]
    public async Task<ActionResult> DeleteRole(long id)
    {
        try
        {
            await _roleService.DeleteRoleAsync(id);
            return Ok(new { message = "Role deleted successfully" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
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
            _logger.LogError(ex, "Error deleting role {RoleId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the role" });
        }
    }

    /// <summary>
    /// Get all available permissions
    /// </summary>
    [HttpGet("permissions")]
    public async Task<ActionResult<List<string>>> GetPermissions()
    {
        try
        {
            var permissions = await _roleService.GetAllPermissionsAsync();
            return Ok(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving permissions");
            return StatusCode(500, new { error = "An error occurred while retrieving permissions" });
        }
    }
}
