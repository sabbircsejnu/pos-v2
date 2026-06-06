using System.Security.Claims;
using RetailPOS.API.Services;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Middleware;

public class AuditContextMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuditContext auditContext, IClientIpResolver ipResolver)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Response.Headers[CorrelationHeader] = correlationId.ToString();

        long? userId = null, roleId = null, outletId = null, businessId = null;
        string? userName = null, roleName = null;
        long? actingUserId = null, actingRoleId = null;

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            userId = TryParseLong(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            roleId = TryParseLong(context.User.FindFirst(RetailPOS.API.Services.RoleSwitchClaims.RealRoleId)?.Value)
                     ?? TryParseLong(context.User.FindFirst("roleId")?.Value);
            outletId = TryParseLong(context.User.FindFirst("outletId")?.Value);
            businessId = TryParseLong(context.User.FindFirst("businessId")?.Value);
            userName = context.User.FindFirst(ClaimTypes.Name)?.Value
                       ?? context.User.FindFirst("name")?.Value;
            roleName = context.User.FindFirst(RetailPOS.API.Services.RoleSwitchClaims.RealRoleName)?.Value
                       ?? context.User.FindFirst(ClaimTypes.Role)?.Value;

            // When a session role-switch is active, record the acting role/outlet from claims
            var isSwitched = string.Equals(
                context.User.FindFirst(RetailPOS.API.Services.RoleSwitchClaims.IsRoleSwitched)?.Value,
                "true", StringComparison.OrdinalIgnoreCase);
            if (isSwitched)
            {
                actingUserId = userId;
                actingRoleId = TryParseLong(context.User.FindFirst(RetailPOS.API.Services.RoleSwitchClaims.ActingRoleId)?.Value);
                outletId = TryParseLong(context.User.FindFirst(RetailPOS.API.Services.RoleSwitchClaims.ActingOutletId)?.Value)
                           ?? outletId;
            }
        }

        // Header overrides remain supported for service-to-service or impersonation tooling
        actingUserId ??= TryParseLong(context.Request.Headers["X-Acting-User-Id"].FirstOrDefault());
        actingRoleId ??= TryParseLong(context.Request.Headers["X-Acting-Role-Id"].FirstOrDefault());

        var ua = context.Request.Headers["User-Agent"].FirstOrDefault();
        var (device, browser, os) = UserAgentParser.Parse(ua);

        var ipResolution = ipResolver.Resolve(context);

        var path = context.Request.Path.Value ?? string.Empty;
        if (context.Request.QueryString.HasValue)
            path = AuditFieldRedaction.SanitizeQueryString(path + context.Request.QueryString.Value);

        auditContext.Initialize(new AuditContextSnapshot(
            CorrelationId: correlationId,
            RealUserId: userId,
            ActingUserId: actingUserId,
            RealRoleId: roleId,
            ActingRoleId: actingRoleId,
            OutletId: outletId,
            BusinessId: businessId,
            Source: AuditSource.UI,
            RequestMethod: context.Request.Method,
            RequestPath: path,
            IpAddress: ipResolution.DetectedIp,
            RawIp: ipResolution.RawIp,
            LocalMachineIp: ipResolution.LocalMachineIp,
            IsLocalRequest: ipResolution.IsLocalRequest,
            UserAgent: ua,
            DeviceName: device,
            Browser: browser,
            Os: os,
            RealUserName: userName,
            ActingRoleName: roleName));

        await _next(context);
    }

    private static Guid ResolveCorrelationId(HttpContext context)
    {
        var header = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (Guid.TryParse(header, out var parsed)) return parsed;
        return Guid.NewGuid();
    }

    private static long? TryParseLong(string? s) =>
        long.TryParse(s, out var v) ? v : null;
}
