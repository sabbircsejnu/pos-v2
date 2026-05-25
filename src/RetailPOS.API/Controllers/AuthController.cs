using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRoleSwitchService _roleSwitch;
    private readonly IAuditService _audit;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, IRoleSwitchService roleSwitch,
        IAuditService audit, ILogger<AuthController> logger)
    {
        _authService = authService;
        _roleSwitch = roleSwitch;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);

            try
            {
                await _audit.RecordAsync(new AuditEventInput
                {
                    ActionType = AuditActionType.Login,
                    Module = AuditModule.Auth,
                    Summary = $"Login successful for {request.Email}",
                    PrimaryEntity = ("User", response.User?.Id.ToString() ?? ""),
                });
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit login success"); }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            try
            {
                await _audit.RecordFailureAsync(AuditActionType.Login, AuditModule.Auth,
                    $"Login failed for {request.Email}", "User", null, ex.Message);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit login failure"); }

            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<LoginResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<LoginResponseDto>> RefreshToken([FromBody] RefreshTokenRequestDto request)
    {
        try
        {
            var response = await _authService.RefreshTokenAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (NotImplementedException)
        {
            return StatusCode(501, new { message = "Refresh token feature coming soon" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "An error occurred during token refresh" });
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var userId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            await _authService.LogoutAsync(userId);

            try
            {
                await _audit.RecordAsync(new AuditEventInput
                {
                    ActionType = AuditActionType.Logout,
                    Module = AuditModule.Auth,
                    Summary = $"Logout for user {userId}",
                    PrimaryEntity = ("User", userId.ToString()),
                });
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit logout"); }

            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { message = "An error occurred during logout" });
        }
    }

    [Authorize]
    [HttpPost("switch-role")]
    public async Task<ActionResult<LoginResponseDto>> SwitchRole([FromBody] RoleSwitchRequestDto request)
    {
        var realUserId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        try
        {
            var response = await _roleSwitch.SwitchRoleAsync(realUserId, request);

            try
            {
                await _audit.RecordAsync(new AuditEventInput
                {
                    ActionType = AuditActionType.RoleSwitch,
                    Module = AuditModule.Auth,
                    Summary = $"User {realUserId} switched to {request.ActingRole}" +
                              (request.ActingOutletId.HasValue ? $" @ outlet {request.ActingOutletId}" : ""),
                    PrimaryEntity = ("User", realUserId.ToString()),
                });
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit role switch"); }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            try
            {
                await _audit.RecordFailureAsync(AuditActionType.RoleSwitch, AuditModule.Auth,
                    $"Role switch denied for user {realUserId}", "User", realUserId.ToString(), ex.Message);
            }
            catch { }
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching role");
            return StatusCode(500, new { message = "An error occurred while switching role" });
        }
    }

    [Authorize]
    [HttpPost("return-owner")]
    public async Task<ActionResult<LoginResponseDto>> ReturnOwner()
    {
        var realUserId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        try
        {
            var response = await _roleSwitch.ReturnOwnerAsync(realUserId);

            try
            {
                await _audit.RecordAsync(new AuditEventInput
                {
                    ActionType = AuditActionType.RoleSwitch,
                    Module = AuditModule.Auth,
                    Summary = $"User {realUserId} returned to Owner Mode",
                    PrimaryEntity = ("User", realUserId.ToString()),
                });
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit return-owner"); }

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error returning to owner mode");
            return StatusCode(500, new { message = "An error occurred while returning to owner mode" });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoDto>> GetCurrentUser()
    {
        try
        {
            var userId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var user = await _authService.GetCurrentUserAsync(userId);
            
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }
}
