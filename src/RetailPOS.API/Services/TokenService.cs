using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;

    public TokenService(IOptions<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public string GenerateAccessToken(User user, Role? role, ActingRoleClaims? acting = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new("userId", user.Id.ToString())
        };

        // Real role always present in token (used for "Return Owner Mode")
        if (role != null)
        {
            claims.Add(new Claim("roleId", role.Id.ToString()));
            claims.Add(new Claim(RoleSwitchClaims.RealRoleId, role.Id.ToString()));
            claims.Add(new Claim(RoleSwitchClaims.RealRoleName, role.Name));
        }

        if (user.OutletId.HasValue)
        {
            claims.Add(new Claim("outletId", user.OutletId.Value.ToString()));
        }

        // Effective role/permissions: acting role wins when present
        if (acting != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, acting.ActingRoleName));
            claims.Add(new Claim(RoleSwitchClaims.ActingRoleId, acting.ActingRoleId.ToString()));
            claims.Add(new Claim(RoleSwitchClaims.ActingRoleName, acting.ActingRoleName));
            claims.Add(new Claim(RoleSwitchClaims.IsRoleSwitched, "true"));
            if (acting.ActingOutletId.HasValue)
                claims.Add(new Claim(RoleSwitchClaims.ActingOutletId, acting.ActingOutletId.Value.ToString()));
            if (!string.IsNullOrEmpty(acting.ActingOutletName))
                claims.Add(new Claim(RoleSwitchClaims.ActingOutletName, acting.ActingOutletName));

            foreach (var p in acting.ActingPermissions)
                claims.Add(new Claim("permission", p));
        }
        else if (role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Name));
            try
            {
                var permissions = JsonSerializer.Deserialize<List<string>>(role.Permissions) ?? new List<string>();
                foreach (var permission in permissions)
                {
                    claims.Add(new Claim("permission", permission));
                }
            }
            catch { }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public bool ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
