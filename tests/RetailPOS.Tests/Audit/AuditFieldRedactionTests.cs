using RetailPOS.Infrastructure.Audit;
using Xunit;

namespace RetailPOS.Tests.Audit;

public class AuditFieldRedactionTests
{
    [Theory]
    [InlineData("User", "password", true)]
    [InlineData("User", "Password", true)]
    [InlineData("User", "PasswordHash", true)]
    [InlineData("User", "password_hash", true)]
    [InlineData("User", "token", true)]
    [InlineData("User", "refreshToken", true)]
    [InlineData("User", "refresh_token", true)]
    [InlineData("User", "secret", true)]
    [InlineData("User", "apikey", true)]
    [InlineData("User", "api_key", true)]
    [InlineData("Customer", "cardNumber", true)]
    [InlineData("Customer", "card_number", true)]
    [InlineData("Customer", "cvv", true)]
    [InlineData("Customer", "pin", true)]
    [InlineData("User", "firstName", false)]
    [InlineData("User", "email", false)]
    [InlineData("Product", "name", false)]
    [InlineData("Product", "price", false)]
    public void IsSensitive_DetectsKnownSensitiveSubstrings(string entityType, string fieldName, bool expected)
    {
        Assert.Equal(expected, AuditFieldRedaction.IsSensitive(entityType, fieldName));
    }

    [Fact]
    public void IsSensitive_EmptyFieldName_ReturnsFalse()
    {
        Assert.False(AuditFieldRedaction.IsSensitive("User", string.Empty));
    }

    [Fact]
    public void IsSensitive_NullFieldName_ReturnsFalse()
    {
        Assert.False(AuditFieldRedaction.IsSensitive("User", null!));
    }

    [Fact]
    public void IsSensitive_CaseInsensitive()
    {
        Assert.True(AuditFieldRedaction.IsSensitive("User", "PASSWORD"));
        Assert.True(AuditFieldRedaction.IsSensitive("User", "PASSWORDHASH"));
        Assert.True(AuditFieldRedaction.IsSensitive("User", "Token"));
    }

    [Fact]
    public void IsSensitive_EntityOverride_PasswordHash()
    {
        // Explicit entity+field override
        Assert.True(AuditFieldRedaction.IsSensitive("User", "PasswordHash"));
    }

    [Theory]
    // Non-sensitive keys are left intact; sensitive keys are masked
    [InlineData("/api/products?search=shoes", "/api/products?search=shoes")]
    [InlineData("/api/users?token=abc123&name=john", "/api/users?token=***REDACTED***&name=john")]
    [InlineData("/api/login?password=secret", "/api/login?password=***REDACTED***")]
    [InlineData("/api/products", "/api/products")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void SanitizeQueryString_MasksSensitiveParams(string? input, string expected)
    {
        Assert.Equal(expected, AuditFieldRedaction.SanitizeQueryString(input));
    }
}
