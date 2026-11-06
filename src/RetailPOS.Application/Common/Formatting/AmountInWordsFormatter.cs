namespace RetailPOS.Application.Common.Formatting;

/// <summary>Writes a currency amount in English words using the Bangladeshi lakh/crore grouping.</summary>
public static class AmountInWordsFormatter
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
        ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Format(decimal amount, string majorUnit, string minorUnit)
    {
        var absolute = Math.Abs(amount);
        var major = decimal.Truncate(absolute);
        var minor = (int)decimal.Truncate((absolute - major) * 100m);

        var words = $"{Words((long)major)} {majorUnit}";
        if (minor > 0)
            words += $" and {Words(minor)} {minorUnit}";

        return $"{(amount < 0m ? "Minus " : "")}{words} Only";
    }

    private static string Words(long number) => number switch
    {
        < 20 => Ones[number],
        < 100 => Tens[number / 10] + Suffix(number % 10),
        < 1_000 => Group(number, 100, "Hundred"),
        < 100_000 => Group(number, 1_000, "Thousand"),
        < 10_000_000 => Group(number, 100_000, "Lakh"),
        _ => Group(number, 10_000_000, "Crore")
    };

    private static string Group(long number, long size, string name) =>
        $"{Words(number / size)} {name}{Suffix(number % size)}";

    private static string Suffix(long remainder) => remainder == 0 ? "" : $" {Words(remainder)}";
}
