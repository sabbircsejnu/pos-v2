using System.Text.RegularExpressions;

namespace RetailPOS.Application.BusinessAdministration;

/// <summary>
/// The "Powered by" receipt footer belongs to the Platform Super Admin (per Business). The tenant-editable
/// Receipt Settings footer keeps printing, but can never carry a "Powered by" line of its own.
/// </summary>
public static partial class BusinessReceiptFooterRules
{
    public const int MaxPoweredByTextLength = 150;

    /// <summary>A single trimmed line, or null when blank. Refuses text over the column limit.</summary>
    public static string? NormalizePoweredByText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = LineBreaks().Replace(text.Trim(), " ");
        if (normalized.Length > MaxPoweredByTextLength)
            throw new ArgumentException($"Powered By text must be at most {MaxPoweredByTextLength} characters.");
        return normalized;
    }

    public static bool ContainsPoweredByLine(string? footer) =>
        !string.IsNullOrEmpty(footer) && SplitLines(footer).Any(line => PoweredByLine().IsMatch(line));

    /// <summary>The footer without any "Powered by" line; null when nothing else remains.</summary>
    public static string? RemovePoweredByLines(string? footer)
    {
        if (!ContainsPoweredByLine(footer))
            return footer;

        var remaining = string.Join('\n', SplitLines(footer!).Where(line => !PoweredByLine().IsMatch(line))).Trim();
        return remaining.Length == 0 ? null : remaining;
    }

    private static string[] SplitLines(string text) => LineBreaks().Split(text);

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"^\W*powered\W*by\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PoweredByLine();
}
