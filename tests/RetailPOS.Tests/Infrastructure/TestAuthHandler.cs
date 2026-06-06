using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailPOS.Tests.Infrastructure;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers.TryGetValue("X-Test-UserId", out var uid)
            && long.TryParse(uid.ToString(), out var parsedUserId)
            ? parsedUserId
            : 1L;

        var businessId = Request.Headers.TryGetValue("X-Test-BusinessId", out var bid)
            && long.TryParse(bid.ToString(), out var parsedBusinessId)
            ? parsedBusinessId
            : 1L;

        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader)
            ? roleHeader.ToString()
            : "AccountsAdmin";

        var permissionsHeader = Request.Headers.TryGetValue("X-Test-Permissions", out var perms)
            ? perms.ToString()
            : "accounts.view,accounts.create,transactions.view,transactions.create";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"test-user-{userId}"),
            new(ClaimTypes.Role, role),
            new("businessId", businessId.ToString()),
            new(RetailPOS.API.Services.RoleSwitchClaims.RealRoleName, role)
        };

        foreach (var permission in permissionsHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
