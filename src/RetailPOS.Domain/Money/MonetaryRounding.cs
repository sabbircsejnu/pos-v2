namespace RetailPOS.Core.Money;

public static class MonetaryRounding
{
    public const int CurrencyDecimals = 2;
    public const MidpointRounding CurrencyMidpointRule = MidpointRounding.AwayFromZero;

    public static decimal ToCurrency(decimal value) =>
        Math.Round(value, CurrencyDecimals, CurrencyMidpointRule);
}
