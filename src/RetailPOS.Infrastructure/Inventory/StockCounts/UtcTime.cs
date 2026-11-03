namespace RetailPOS.Infrastructure.StockCounts;

internal static class UtcTime
{
    // Npgsql legacy timestamp mode can materialize timestamptz values as Local; compare and expose them as UTC.
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
