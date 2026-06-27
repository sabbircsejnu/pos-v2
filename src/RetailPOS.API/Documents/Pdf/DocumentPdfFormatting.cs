using System.Globalization;

namespace RetailPOS.API.Documents.Pdf;

public static class DocumentPdfFormatting
{
    public static string FormatDate(DateTime value, DocumentPdfFormattingOptions options)
    {
        var converted = ConvertToBusinessTime(value, options.TimeZone);
        return converted.ToString(string.IsNullOrWhiteSpace(options.DateFormat) ? "dd/MM/yyyy" : options.DateFormat, CultureInfo.InvariantCulture);
    }

    public static string FormatDateTime(DateTime value, DocumentPdfFormattingOptions options)
    {
        var converted = ConvertToBusinessTime(value, options.TimeZone);
        return converted.ToString("dd MMM, yyyy hh:mm tt", CultureInfo.InvariantCulture);
    }

    public static string FormatCurrency(decimal value, DocumentPdfFormattingOptions options)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalDigits = Math.Clamp(options.DecimalPlaces, 0, 6);
        format.NumberGroupSeparator = string.IsNullOrWhiteSpace(options.ThousandsSeparator) ? "," : options.ThousandsSeparator;
        format.NumberDecimalSeparator = string.IsNullOrWhiteSpace(options.DecimalSeparator) ? "." : options.DecimalSeparator;

        var numericValue = value.ToString("N", format);
        var symbol = string.IsNullOrWhiteSpace(options.CurrencySymbol) ? options.CurrencyCode : options.CurrencySymbol;
        var symbolPosition = string.IsNullOrWhiteSpace(options.SymbolPosition) ? "before" : options.SymbolPosition.Trim().ToLowerInvariant();

        return symbolPosition == "after"
            ? $"{numericValue} {symbol}".Trim()
            : $"{symbol} {numericValue}".Trim();
    }

    public static IReadOnlyList<string> SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static DateTime ConvertToBusinessTime(DateTime value, string? timeZoneId)
    {
        var sourceUtc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return sourceUtc;
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(sourceUtc, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            return sourceUtc;
        }
        catch (InvalidTimeZoneException)
        {
            return sourceUtc;
        }
    }
}