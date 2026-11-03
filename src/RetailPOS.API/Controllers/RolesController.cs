using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using MediatR;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Application.Identity.Queries;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "roles.view")]
public class RolesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IRoleSwitchContext _roleSwitchContext;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        ISender sender,
        IRoleSwitchContext roleSwitchContext,
        ITenantAccessService tenantAccess,
        ILogger<RolesController> logger)
    {
        _sender = sender;
        _roleSwitchContext = roleSwitchContext;
        _tenantAccess = tenantAccess;
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
            var businessId = _tenantAccess.IsSuperAdmin
                ? null
                : _tenantAccess.EffectiveBusinessId;
            var roles = await _sender.Send(
                new GetRolesQuery(_roleSwitchContext.IsSuperAdmin, businessId),
                HttpContext.RequestAborted);
            return Ok(roles.Select(ToDto).ToArray());
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<RoleDto>> GetRole(long id, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _sender.Send(
                new GetRoleByIdQuery(id, _roleSwitchContext.IsSuperAdmin),
                cancellationToken);
            return Ok(ToDto(role));
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
            _logger.LogError(ex, "Error retrieving role {RoleId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the role" });
        }
    }

    /// <summary>
    /// Create new role
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "roles.create")]
    public async Task<ActionResult<RoleDto>> CreateRole(
        [FromBody] CreateRoleDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var role = await _sender.Send(
                new CreateRoleCommand(dto.Name, dto.Permissions),
                cancellationToken);
            return CreatedAtAction(nameof(GetRole), new { id = role.Id }, ToDto(role));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ValidationException ex)
        {
            return ToValidationProblem(ex);
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
    public async Task<ActionResult<RoleDto>> UpdateRole(
        long id,
        [FromBody] UpdateRoleDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var role = await _sender.Send(
                new UpdateRoleCommand(id, dto.Name, dto.Permissions),
                cancellationToken);
            return Ok(ToDto(role));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ValidationException ex)
        {
            return ToValidationProblem(ex);
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
    public async Task<ActionResult> DeleteRole(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteRoleCommand(id), cancellationToken);
            return Ok(new { message = "Role deleted successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<List<string>>> GetPermissions(CancellationToken cancellationToken)
    {
        try
        {
            var permissions = await _sender.Send(new GetPermissionsQuery(), cancellationToken);
            return Ok(permissions.ToList());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving permissions");
            return StatusCode(500, new { error = "An error occurred while retrieving permissions" });
        }
    }

    private static RoleDto ToDto(RoleReadModel role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Permissions = role.Permissions.ToList(),
        UserCount = role.UserCount,
        CreatedAt = role.CreatedAt,
        UpdatedAt = role.UpdatedAt
    };

    private ActionResult ToValidationProblem(ValidationException exception)
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        foreach (var failure in exception.Errors)
        {
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }

        return ValidationProblem(modelState);
    }
}
