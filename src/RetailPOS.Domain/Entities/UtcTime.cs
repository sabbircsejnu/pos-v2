namespace RetailPOS.Core.Entities;

/// <summary>
/// Normalises instants at the storage boundary. With Npgsql's legacy timestamp behaviour a <c>timestamptz</c> may be
/// read as <see cref="DateTimeKind.Local"/>; comparing such a value with <c>UtcNow</c> silently shifts it by the host
/// offset. Every instant that drives a financial decision is converted to a true UTC <see cref="DateTime"/> first.
/// </summary>
public static class UtcTime
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? Normalize(DateTime? value) => value.HasValue ? Normalize(value.Value) : null;
}
