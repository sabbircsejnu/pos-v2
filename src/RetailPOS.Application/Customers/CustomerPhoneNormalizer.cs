using System.Text.RegularExpressions;

namespace RetailPOS.Application.Customers;

/// <summary>
/// Canonical phone format used to detect duplicate customers within a Business (LLD §9).
/// Bangladesh mobile numbers (01[3-9]XXXXXXXX, 8801…, +8801…) all become <c>+8801XXXXXXXXX</c>. Any other number must be
/// written in international form (<c>+</c> followed by 8–15 digits) and is kept as <c>+digits</c>. Normalisation is a lookup
/// aid only: it proves nothing about who owns the number. The SQL used by the Phase 2 backfill follows the same rules.
/// </summary>
public static partial class CustomerPhoneNormalizer
{
    public const int MaxLength = 20;

    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var compact = NonDigitOrPlus().Replace(phone, string.Empty);
        var plus = compact.StartsWith('+');
        var digits = compact.Replace("+", string.Empty);
        if (digits.Length == 0 || compact.LastIndexOf('+') > 0)
            return null;

        var candidate = plus ? "+" + digits : digits;
        if (BangladeshLocal().IsMatch(candidate))
            return "+88" + candidate;
        if (BangladeshInternational().IsMatch(candidate))
            return "+" + candidate;
        if (BangladeshInternationalPlus().IsMatch(candidate))
            return candidate;
        if (InternationalE164().IsMatch(candidate))
            return candidate;

        return null;
    }

    /// <summary>Digits-only form used for partial (lookup) matching against stored normalized values.</summary>
    public static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    [GeneratedRegex("[^0-9+]")]
    private static partial Regex NonDigitOrPlus();

    [GeneratedRegex(@"^01[3-9][0-9]{8}$")]
    private static partial Regex BangladeshLocal();

    [GeneratedRegex(@"^8801[3-9][0-9]{8}$")]
    private static partial Regex BangladeshInternational();

    [GeneratedRegex(@"^\+8801[3-9][0-9]{8}$")]
    private static partial Regex BangladeshInternationalPlus();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex InternationalE164();
}

/// <summary>Another customer of the same Business already owns this normalized phone; maps to HTTP 409.</summary>
public sealed class CustomerPhoneConflictException(CustomerReadModel existing)
    : InvalidOperationException($"A customer with phone '{existing.Phone}' already exists")
{
    public CustomerReadModel Existing { get; } = existing;
}
