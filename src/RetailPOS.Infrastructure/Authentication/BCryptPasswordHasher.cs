using RetailPOS.Application.Authentication.Abstractions;

namespace RetailPOS.Infrastructure.Authentication;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);

    public string Hash(string password, int? workFactor = null) =>
        workFactor.HasValue
            ? BCrypt.Net.BCrypt.HashPassword(password, workFactor.Value)
            : BCrypt.Net.BCrypt.HashPassword(password);
}
