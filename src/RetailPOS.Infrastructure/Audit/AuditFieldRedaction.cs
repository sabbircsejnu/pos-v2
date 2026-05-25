namespace RetailPOS.Infrastructure.Audit;

public static class AuditFieldRedaction
{
    public const string RedactedToken = "***REDACTED***";

    private static readonly string[] SensitiveSubstrings =
    {
        "password",
        "passwordhash",
        "password_hash",
        "token",
        "refreshtoken",
        "refresh_token",
        "secret",
        "apikey",
        "api_key",
        "cardnumber",
        "card_number",
        "cvv",
        "pin",
        "otp",
        "private_key",
        "privatekey",
    };

    private static readonly HashSet<string> EntityFieldOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "User.PasswordHash",
        "User.password_hash",
    };

    public static bool IsSensitive(string entityType, string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;

        if (EntityFieldOverrides.Contains($"{entityType}.{fieldName}"))
            return true;

        var lower = fieldName.ToLowerInvariant().Replace("_", string.Empty);
        foreach (var s in SensitiveSubstrings)
        {
            var n = s.Replace("_", string.Empty);
            if (lower.Contains(n)) return true;
        }
        return false;
    }

    public static string SanitizeQueryString(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
        var qIdx = path.IndexOf('?');
        if (qIdx < 0) return path;

        var basePart = path[..qIdx];
        var query = path[(qIdx + 1)..];
        var pairs = query.Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            var eq = pairs[i].IndexOf('=');
            var key = eq < 0 ? pairs[i] : pairs[i][..eq];
            if (IsSensitive("", key))
            {
                pairs[i] = $"{key}={RedactedToken}";
            }
        }
        return $"{basePart}?{string.Join('&', pairs)}";
    }
}
