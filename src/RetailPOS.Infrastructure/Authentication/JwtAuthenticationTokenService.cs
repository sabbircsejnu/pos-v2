using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Authentication;

public sealed class JwtAuthenticationTokenService(IOptions<JwtTokenSettings> settings)
    : IAuthenticationTokenService
{
    private readonly JwtTokenSettings _settings = settings.Value;

    public string GenerateAccessToken(
        AuthenticationTokenUser user,
        AuthenticationTokenRole? role,
        AuthenticationActingRoleClaims? acting = null,
        IReadOnlyCollection<string>? effectivePermissions = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new("userId", user.Id.ToString())
        };

        if (role is not null)
        {
            claims.Add(new Claim("roleId", role.Id.ToString()));
            claims.Add(new Claim(AuthenticationClaimNames.RealRoleId, role.Id.ToString()));
            claims.Add(new Claim(AuthenticationClaimNames.RealRoleName, role.Name));
        }

        if (user.OutletId.HasValue)
        {
            claims.Add(new Claim("outletId", user.OutletId.Value.ToString()));
        }

        if (user.BusinessId.HasValue)
        {
            claims.Add(new Claim("businessId", user.BusinessId.Value.ToString()));
        }

        if (acting is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, acting.ActingRoleName));
            claims.Add(new Claim(AuthenticationClaimNames.ActingRoleId, acting.ActingRoleId.ToString()));
            claims.Add(new Claim(AuthenticationClaimNames.ActingRoleName, acting.ActingRoleName));
            claims.Add(new Claim(AuthenticationClaimNames.IsRoleSwitched, "true"));
            if (acting.ActingOutletId.HasValue)
            {
                claims.Add(new Claim(
                    AuthenticationClaimNames.ActingOutletId,
                    acting.ActingOutletId.Value.ToString()));
            }
            if (!string.IsNullOrEmpty(acting.ActingOutletName))
            {
                claims.Add(new Claim(AuthenticationClaimNames.ActingOutletName, acting.ActingOutletName));
            }

            claims.AddRange(acting.ActingPermissions.Select(permission => new Claim("permission", permission)));
        }
        else if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Name));
            var permissions = effectivePermissions ?? ParsePermissions(role.Permissions);
            claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt
                || !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or SecurityTokenException
                or FormatException
                or InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParsePermissions(string permissionsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(permissionsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
