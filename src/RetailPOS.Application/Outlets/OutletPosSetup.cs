using System.Text.RegularExpressions;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Outlets;

public sealed record OutletTerminalReadModel(
    long Id,
    string Name,
    string Code,
    bool IsActive,
    bool HasHistory,
    bool HasOpenSession);

/// <summary>A terminal row of the Outlet form. A null <see cref="Id"/> creates a terminal; otherwise it edits one.</summary>
public sealed record OutletTerminalInput(long? Id, string Name, bool IsActive = true);

/// <summary>
/// The POS-related part of an Outlet create/edit request (FR-INV-01, FR-TERM-04). Null members are left unchanged:
/// <see cref="Terminals"/> null leaves every terminal alone, and an empty <see cref="TimeZoneId"/> clears the override.
/// </summary>
public sealed record OutletPosSetupInput(
    string? InvoicePrefix,
    string? TimeZoneId,
    IReadOnlyList<OutletTerminalInput>? Terminals,
    bool CanManageTerminals)
{
    public static OutletPosSetupInput None { get; } = new(null, null, null, false);
}

public static class OutletSetupErrorCodes
{
    public const string InvalidInvoicePrefix = "OUTLET_INVOICE_PREFIX_INVALID";
    public const string DuplicateInvoicePrefix = "OUTLET_INVOICE_PREFIX_EXISTS";
    public const string InvoicePrefixLocked = "OUTLET_INVOICE_PREFIX_LOCKED";
    public const string InvoicePrefixAmbiguous = "OUTLET_INVOICE_PREFIX_AMBIGUOUS";
    public const string DuplicateTerminalName = "TERMINAL_NAME_EXISTS";
    public const string InvalidTerminal = "TERMINAL_INVALID";
    public const string TerminalHasOpenSession = "TERMINAL_HAS_OPEN_SESSION";
    public const string TerminalHasHistory = "TERMINAL_HAS_HISTORY";
}

/// <summary>A refused Outlet POS-setup change; maps to HTTP 409 with a stable code.</summary>
public sealed class OutletSetupException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static partial class OutletPosSetupRules
{
    public const int MaxTerminalNameLength = 120;

    public static string NormalizeInvoicePrefix(string? prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (!InvoicePrefixPattern().IsMatch(normalized))
            throw new OutletSetupException(
                OutletSetupErrorCodes.InvalidInvoicePrefix,
                "The invoice prefix must start with a letter, use only letters and digits and be at most 10 characters.");
        return normalized;
    }

    /// <summary>
    /// Two prefixes would produce the same invoice number when one extends the other with digits only
    /// (prefix + yymmdd + sequence is all digits after the prefix), so such a pair is refused.
    /// </summary>
    public static bool CanProduceCollision(string first, string second)
    {
        if (string.Equals(first, second, StringComparison.Ordinal))
            return true;
        var (shorter, longer) = first.Length < second.Length ? (first, second) : (second, first);
        return longer.StartsWith(shorter, StringComparison.Ordinal)
            && longer[shorter.Length..].All(char.IsAsciiDigit);
    }

    public static string? NormalizeTimeZone(string? timeZoneId)
    {
        if (timeZoneId is null)
            return null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return string.Empty;
        BusinessPosSettingsRules.ValidateTimeZone(timeZoneId);
        return timeZoneId.Trim();
    }

    public static string NormalizeTerminalName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaxTerminalNameLength)
            throw new OutletSetupException(
                OutletSetupErrorCodes.InvalidTerminal,
                $"A terminal name is required and must be at most {MaxTerminalNameLength} characters.");
        return normalized;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{0,9}$")]
    private static partial Regex InvoicePrefixPattern();
}
