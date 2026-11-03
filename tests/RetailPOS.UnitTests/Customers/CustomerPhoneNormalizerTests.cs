using RetailPOS.Application.Customers;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerPhoneNormalizerTests
{
    [Theory]
    [InlineData("01712345678", "+8801712345678")]
    [InlineData("01712-345678", "+8801712345678")]
    [InlineData("8801712345678", "+8801712345678")]
    [InlineData("+8801712345678", "+8801712345678")]
    [InlineData("+880 1712 345 678", "+8801712345678")]
    [InlineData("(017) 12-345678", "+8801712345678")]
    [InlineData("+1 (555) 100-1234", "+15551001234")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    public void EquivalentFormatsNormaliseToOneCanonicalValue(string input, string expected) =>
        Assert.Equal(expected, CustomerPhoneNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("555-0142")]
    [InlineData("01212345678")]
    [InlineData("+0123456789")]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    [InlineData("12+345678901")]
    [InlineData("abc")]
    public void NumbersThatCannotBeCanonicalisedReturnNull(string? input) =>
        Assert.Null(CustomerPhoneNormalizer.Normalize(input));

    [Fact]
    public void DigitsOnlyStripsEverythingElse() =>
        Assert.Equal("8801712", CustomerPhoneNormalizer.DigitsOnly("+880 1712"));
}
