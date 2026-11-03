namespace RetailPOS.Application.Authentication.Abstractions;

public interface IPasswordHasher
{
    bool Verify(string password, string passwordHash);
    string Hash(string password, int? workFactor = null);
}
