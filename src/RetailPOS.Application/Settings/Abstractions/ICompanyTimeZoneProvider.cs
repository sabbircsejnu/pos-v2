using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos;

namespace RetailPOS.Application.Settings.Abstractions;

/// <summary>The time zone that decides every calendar date of a Business.</summary>
public interface ICompanyTimeZoneProvider
{
    Task<string> GetTimeZoneIdAsync(long businessId, CancellationToken cancellationToken);
}

/// <summary>
/// The zone saved for the Business; a Business that has never saved one keeps the shared Company Settings value.
/// </summary>
public sealed class CompanyTimeZoneProvider(
    IBusinessPosSettingsStore businessSettings,
    ISystemSettingsStore systemSettings) : ICompanyTimeZoneProvider
{
    public async Task<string> GetTimeZoneIdAsync(long businessId, CancellationToken cancellationToken)
    {
        var business = await businessSettings.GetAsync(businessId, cancellationToken);
        var zone = business.IsPersisted
            ? business.DefaultTimeZoneId
            : (await systemSettings.GetAsync(cancellationToken)).Company.TimeZone;
        return CashSessionTime.ResolveEffectiveTimeZoneId(null, zone);
    }
}

/// <summary>The Company time zone of the current request: the Business's, or the shared value outside any Business.</summary>
public interface ICurrentCompanyTimeZone
{
    Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken);
}

public sealed class CurrentCompanyTimeZone(
    IBusinessScope businessScope,
    ICompanyTimeZoneProvider timeZones,
    ISystemSettingsStore systemSettings) : ICurrentCompanyTimeZone
{
    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken) =>
        businessScope.EffectiveBusinessId is long businessId
            ? await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken)
            : CashSessionTime.ResolveEffectiveTimeZoneId(
                null,
                (await systemSettings.GetAsync(cancellationToken)).Company.TimeZone);
}

public static class CompanyTimeZoneProviderExtensions
{
    /// <summary>Today's calendar date in the Business's Company time zone.</summary>
    public static async Task<DateTime> GetTodayAsync(
        this ICompanyTimeZoneProvider provider,
        long businessId,
        CancellationToken cancellationToken) =>
        CompanyDates.DateFor(
            DateTime.UtcNow,
            await provider.GetTimeZoneIdAsync(businessId, cancellationToken));
}
