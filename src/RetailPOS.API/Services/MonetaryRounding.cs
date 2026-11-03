namespace RetailPOS.API.Services;

public static class MonetaryRounding
{
    public const int CurrencyDecimals = RetailPOS.Core.Money.MonetaryRounding.CurrencyDecimals;
    public const MidpointRounding CurrencyMidpointRule = RetailPOS.Core.Money.MonetaryRounding.CurrencyMidpointRule;

    public static decimal ToCurrency(decimal value) =>
        RetailPOS.Core.Money.MonetaryRounding.ToCurrency(value);
}
