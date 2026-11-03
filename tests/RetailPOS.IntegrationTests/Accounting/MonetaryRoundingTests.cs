using RetailPOS.API.Services;

namespace RetailPOS.Tests.Accounting;

public class MonetaryRoundingTests
{
    [Theory]
    [InlineData(1.005, 1.01)]
    [InlineData(-1.005, -1.01)]
    [InlineData(1.004, 1.00)]
    [InlineData(1.006, 1.01)]
    public void ToCurrency_UsesExplicitAwayFromZeroPolicy(decimal value, decimal expected)
    {
        Assert.Equal(expected, MonetaryRounding.ToCurrency(value));
    }
}
