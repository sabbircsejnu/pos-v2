namespace RetailPOS.Application.Settings.Abstractions;

/// <summary>
/// Effective Business POS configuration (D3, D9, P2.1, P2.2). <see cref="IsPersisted"/> is false while the Business
/// has never saved settings; the values are then the platform defaults (tax falls back to the legacy tax settings).
/// </summary>
public sealed record BusinessPosSettingsSnapshot(
    long BusinessId,
    int MaxCashSessionDurationHours,
    string DefaultTimeZoneId,
    bool TaxEnabled,
    decimal DefaultTaxRate,
    string TaxName,
    bool PricesIncludeTax,
    decimal MaxManualDiscountPercent,
    long Version,
    DateTime? UpdatedAt,
    long? UpdatedByUserId,
    bool IsPersisted,
    bool AllowOwnerExpenseSelfApproval = false);

/// <summary>A partial update: null members are left unchanged.</summary>
public sealed record BusinessPosSettingsUpdate(
    int? MaxCashSessionDurationHours = null,
    string? DefaultTimeZoneId = null,
    bool? TaxEnabled = null,
    decimal? DefaultTaxRate = null,
    string? TaxName = null,
    bool? PricesIncludeTax = null,
    decimal? MaxManualDiscountPercent = null,
    bool? AllowOwnerExpenseSelfApproval = null);

public interface IBusinessPosSettingsStore
{
    Task<BusinessPosSettingsSnapshot> GetAsync(long businessId, CancellationToken cancellationToken);

    /// <summary>Applies the update, bumps the version and returns the previous and new snapshots for auditing.</summary>
    Task<(BusinessPosSettingsSnapshot Before, BusinessPosSettingsSnapshot After)> UpdateAsync(
        long businessId,
        BusinessPosSettingsUpdate update,
        long? userId,
        CancellationToken cancellationToken);
}

public static class BusinessPosSettingsRules
{
    public static void Validate(BusinessPosSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.MaxCashSessionDurationHours is < 1 or > RetailPOS.Core.Entities.BusinessPosSettings.MaxCashSessionDurationHoursLimit)
            throw new InvalidOperationException(
                $"Maximum cash session duration must be between 1 and {RetailPOS.Core.Entities.BusinessPosSettings.MaxCashSessionDurationHoursLimit} hours.");
        if (update.DefaultTaxRate is < 0m or > 100m)
            throw new InvalidOperationException("Default tax rate must be between 0 and 100.");
        if (update.MaxManualDiscountPercent is < 0m or > 100m)
            throw new InvalidOperationException("Maximum manual discount must be between 0 and 100 percent.");
        if (update.TaxName is not null && string.IsNullOrWhiteSpace(update.TaxName))
            throw new InvalidOperationException("Tax name cannot be blank.");
        if (update.DefaultTimeZoneId is not null)
            ValidateTimeZone(update.DefaultTimeZoneId);
    }

    public static void ValidateTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 64)
            throw new InvalidOperationException("A valid IANA time zone identifier is required.");
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"'{timeZoneId}' is not a recognised IANA time zone.");
        }
    }
}
