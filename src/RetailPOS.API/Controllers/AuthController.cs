using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Application.Authentication.Queries;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly ISender _sender;

    public AuthController(
        ILogger<AuthController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponseDto>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = ToResponse(await _sender.Send(
                new LoginCommand(request.Email, request.Password, request.RememberMe),
                cancellationToken));

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Login,
                    AuditModule.Auth,
                    $"Login successful for {request.Email}",
                    "User",
                    response.User?.Id.ToString() ?? "")), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit login success"); }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            try
            {
                await _sender.Send(new RecordAuditEventFailureCommand(
                    AuditActionType.Login,
                    AuditModule.Auth,
                    $"Login failed for {request.Email}",
                    "User",
                    null,
                    ex.Message), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit login failure"); }

            return Unauthorized(new { message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    [HttpPost("register")]
    [Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<LoginResponseDto>> Register(
        [FromBody] RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = ToResponse(await _sender.Send(
                new RegisterCommand(request.Name, request.Email, request.Password, request.ConfirmPassword),
                cancellationToken));
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    [HttpPost("complete-invitation")]
    public async Task<ActionResult<LoginResponseDto>> CompleteInvitation(
        [FromBody] CompleteInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = ToResponse(await _sender.Send(
                new CompleteInvitationCommand(request.Token, request.NewPassword),
                cancellationToken));
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FluentValidation.ValidationException ex)
        {
            return BadRequest(new { message = ex.Errors.FirstOrDefault()?.ErrorMessage });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing invitation");
            return StatusCode(500, new { message = "An error occurred while completing invitation" });
        }
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth-refresh")]
    public async Task<ActionResult<LoginResponseDto>> RefreshToken(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = ToResponse(await _sender.Send(
                new RefreshTokenCommand(request.Token, request.RefreshToken),
                cancellationToken));

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.TokenRefresh,
                    AuditModule.Auth,
                    $"Token refresh successful for user {response.User?.Id}",
                    "User",
                    response.User?.Id.ToString() ?? "")), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit token refresh success"); }

            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            try
            {
                var isReuseAttempt = ex.Message.Contains("reuse", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("revoked", StringComparison.OrdinalIgnoreCase);

                if (isReuseAttempt)
                {
                    await _sender.Send(new RecordAuditEventFailureCommand(
                        AuditActionType.TokenReuseAttempt,
                        AuditModule.Auth,
                        "Revoked refresh token reuse attempt",
                        "Auth",
                        null,
                        ex.Message), cancellationToken);
                }
                else
                {
                    await _sender.Send(new RecordAuditEventFailureCommand(
                        AuditActionType.TokenRefresh,
                        AuditModule.Auth,
                        "Token refresh failed",
                        "Auth",
                        null,
                        ex.Message), cancellationToken);
                }
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit token refresh failure"); }

            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "An error occurred during token refresh" });
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            await _sender.Send(
                new LogoutCommand(userId, request.RefreshToken),
                cancellationToken);

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Logout,
                    AuditModule.Auth,
                    $"Logout for user {userId}",
                    "User",
                    userId.ToString())), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit logout"); }

            return Ok(new { message = "Logged out successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { message = "An error occurred during logout" });
        }
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            await _sender.Send(new LogoutAllCommand(userId), cancellationToken);

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Logout,
                    AuditModule.Auth,
                    $"All sessions logged out for user {userId}",
                    "User",
                    userId.ToString())), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit all-session logout"); }

            return Ok(new { message = "All sessions logged out successfully" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during all-session logout");
            return StatusCode(500, new { message = "An error occurred during logout" });
        }
    }

    /// <summary>The roles and locations the caller may switch to; never includes anything else.</summary>
    [Authorize]
    [HttpGet("switch-options")]
    public async Task<ActionResult<UserContextOptionsDto>> GetSwitchOptions(CancellationToken cancellationToken)
    {
        var realUserId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var options = await _sender.Send(new GetUserContextOptionsQuery(realUserId), cancellationToken);
        return Ok(new UserContextOptionsDto
        {
            CanSwitch = options.CanSwitch,
            AllowAllLocations = options.AllowAllLocations,
            Roles = options.Roles.Select(role => new UserContextOptionDto { Id = role.Id, Name = role.Name }).ToList(),
            Outlets = options.Outlets.Select(location => new UserContextOptionDto { Id = location.Id, Name = location.Name }).ToList(),
            Warehouses = options.Warehouses.Select(location => new UserContextOptionDto { Id = location.Id, Name = location.Name }).ToList()
        });
    }

    [Authorize]
    [HttpPost("switch-role")]
    public async Task<ActionResult<LoginResponseDto>> SwitchRole(
        [FromBody] RoleSwitchRequestDto request,
        CancellationToken cancellationToken)
    {
        var realUserId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        try
        {
            var response = ToResponse(await _sender.Send(
                new SwitchRoleCommand(
                    realUserId,
                    request.ActingRole,
                    request.ActingOutletId,
                    request.ActingLocationType,
                    request.ActingLocationId),
                cancellationToken));

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.RoleSwitch,
                    AuditModule.Auth,
                    $"User {realUserId} switched to {response.User.ActingRoleName}"
                        + $" @ {response.User.ActingLocationType}"
                        + ((response.User.ActingOutletId ?? response.User.ActingWarehouseId) is long locationId
                            ? $" {locationId}"
                            : ""),
                    "User",
                    realUserId.ToString())), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit role switch"); }

            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            try
            {
                await _sender.Send(new RecordAuditEventFailureCommand(
                    AuditActionType.RoleSwitch,
                    AuditModule.Auth,
                    $"Role switch denied for user {realUserId}",
                    "User",
                    realUserId.ToString(),
                    ex.Message), cancellationToken);
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
    public async Task<ActionResult<LoginResponseDto>> ReturnOwner(CancellationToken cancellationToken)
    {
        var realUserId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        try
        {
            var response = ToResponse(await _sender.Send(
                new ReturnOwnerCommand(
                    realUserId,
                    string.Equals(
                        User.FindFirst(RoleSwitchClaims.IsRoleSwitched)?.Value,
                        "true",
                        StringComparison.OrdinalIgnoreCase)),
                cancellationToken));

            try
            {
                await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.RoleSwitch,
                    AuditModule.Auth,
                    $"User {realUserId} returned to their own context",
                    "User",
                    realUserId.ToString())), cancellationToken);
            }
            catch (Exception auditEx) { _logger.LogWarning(auditEx, "Failed to audit return-owner"); }

            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
    public async Task<ActionResult<UserInfoDto>> GetCurrentUser(CancellationToken cancellationToken)
    {
        try
        {
            var userId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var profile = await _sender.Send(new GetCurrentUserQuery(userId), cancellationToken);

            if (profile == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var result = ToUserInfo(profile);
            if (HttpContext.Items[RetailPOS.API.Middleware.BusinessContextMiddleware.ItemName] is long selected)
                result.BusinessId = selected;
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]
    [HttpPost("business-context")]
    [ProducesResponseType(typeof(BusinessContextResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BusinessContextResult>> SelectBusinessContext(
        [FromBody] SelectBusinessContextRequest request, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        try
        {
            return Ok(await _sender.Send(new ValidateBusinessContextQuery(userId, request.BusinessId),
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning("Business selection denied for user {UserId}: {Reason}", userId, exception.Message);
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>Validates an explicit Super Admin Business selection; null clears the selection.</summary>
    public sealed record SelectBusinessContextRequest(long? BusinessId);

    private static LoginResponseDto ToResponse(AuthenticationResult result) =>
        new()
            {
                Token = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresAt = result.ExpiresAt,
                User = new UserInfoDto
                {
                    Id = result.User.Id,
                    BusinessId = result.User.BusinessId,
                    Name = result.User.Name,
                    Email = result.User.Email,
                    RoleName = result.User.RoleName,
                    Permissions = result.User.Permissions.ToList(),
                    OutletId = result.User.OutletId,
                    OutletName = result.User.OutletName,
                    RealRoleName = result.User.RealRoleName,
                    ActingRoleName = result.User.ActingRoleName,
                    ActingOutletId = result.User.ActingOutletId,
                    ActingOutletName = result.User.ActingOutletName,
                    ActingLocationType = result.User.ActingLocationType,
                    ActingWarehouseId = result.User.ActingWarehouseId,
                    ActingWarehouseName = result.User.ActingWarehouseName,
                    IsRoleSwitched = result.User.IsRoleSwitched,
                    IsBusinessOwner = result.User.IsBusinessOwner,
                    MustResetPassword = result.User.MustResetPassword
                }
            };

    private static UserInfoDto ToUserInfo(AuthenticationUserProfile profile) =>
        new()
        {
            Id = profile.Id,
            BusinessId = profile.BusinessId,
            Name = profile.Name,
            Email = profile.Email,
            RoleName = profile.RoleName,
            Permissions = profile.Permissions.ToList(),
            OutletId = profile.OutletId,
            OutletName = profile.OutletName,
            RealRoleName = profile.RealRoleName,
            ActingRoleName = profile.ActingRoleName,
            ActingOutletId = profile.ActingOutletId,
            ActingOutletName = profile.ActingOutletName,
            ActingLocationType = profile.ActingLocationType,
            ActingWarehouseId = profile.ActingWarehouseId,
            ActingWarehouseName = profile.ActingWarehouseName,
            IsRoleSwitched = profile.IsRoleSwitched,
            IsBusinessOwner = profile.IsBusinessOwner,
            MustResetPassword = profile.MustResetPassword
        };
}
