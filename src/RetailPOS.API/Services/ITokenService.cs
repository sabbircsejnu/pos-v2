using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user, Role? role);
    string GenerateRefreshToken();
    bool ValidateToken(string token);
}
