using System.Text.Json;
using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings;

namespace RetailPOS.Application.Settings.Queries;

public sealed record GetSystemSettingsQuery : IRequest<SystemSettings>;

/// <summary>Inside a Business, the Company time zone is that Business's own; the shared value is never mutated.</summary>
public sealed class GetSystemSettingsQueryHandler(
    ISystemSettingsStore store,
    IBusinessScope businessScope,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<GetSystemSettingsQuery, SystemSettings>
{
    public async Task<SystemSettings> Handle(
        GetSystemSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken);
        if (businessScope.EffectiveBusinessId is not long businessId)
            return settings;

        return WithCompanyTimeZone(settings, await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
    }

    internal static SystemSettings WithCompanyTimeZone(SystemSettings settings, string timeZoneId)
    {
        if (string.Equals(timeZoneId, settings.Company.TimeZone, StringComparison.Ordinal))
            return settings;

        var scoped = JsonSerializer.Deserialize<SystemSettings>(JsonSerializer.Serialize(settings))!;
        scoped.Company.TimeZone = timeZoneId;
        return scoped;
    }
}
