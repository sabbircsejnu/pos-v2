using System.Globalization;

namespace RetailPOS.Application.Common.Formatting;

public static class UiDateFormatting
{
    public static string Normalize(string format) => format.ToLowerInvariant() switch
    {
        "dd/mm/yyyy" => "dd/MM/yyyy",
        "mm/dd/yyyy" => "MM/dd/yyyy",
        "yyyy-mm-dd" => "yyyy-MM-dd",
        _ => throw new ArgumentException("Unsupported company date format.")
    };

    public static string Format(object value, string format, bool includeTime = true) => value switch
    {
        DateOnly date => date.ToString(Normalize(format), CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToString(
            Normalize(format) + (includeTime ? " HH:mm:ss" : ""), CultureInfo.InvariantCulture),
        DateTimeOffset timestamp => timestamp.UtcDateTime.ToString(
            Normalize(format) + (includeTime ? " HH:mm:ss" : ""), CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
