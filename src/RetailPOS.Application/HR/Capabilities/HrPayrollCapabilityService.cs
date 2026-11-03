using RetailPOS.Application.Authorization;

namespace RetailPOS.Application.HR.Capabilities;

public sealed class HrPayrollCapabilityService(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityReadStore readStore) : IHrPayrollCapabilityService
{
    public async Task<HrPayrollCapabilities> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var businessId = scope.RequireBusinessId();
        if (businessId <= 0)
            throw new BusinessContextRequiredException("A validated Business scope is required.");

        var settings = await readStore.GetExplicitSettingsAsync(businessId, cancellationToken);
        var hr = IsExplicitlyEnabled(settings, HrPayrollCapabilityKeys.Hr);
        var attendance = hr && IsExplicitlyEnabled(settings, HrPayrollCapabilityKeys.Attendance);
        return new HrPayrollCapabilities(
            businessId,
            hr,
            hr && IsExplicitlyEnabled(settings, HrPayrollCapabilityKeys.Payroll),
            attendance,
            attendance && IsExplicitlyEnabled(settings, HrPayrollCapabilityKeys.DeviceIntegration));
    }

    public async Task EnsureEnabledAsync(string capability, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        var normalized = capability.Trim().ToLowerInvariant();
        if (!HrPayrollCapabilityKeys.All.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException("Unknown HR/Payroll capability.", nameof(capability));

        var capabilities = await GetAsync(cancellationToken);
        if (!capabilities.IsEnabled(normalized))
            throw new UnauthorizedAccessException("The required HR/Payroll capability is disabled.");
    }

    private static bool IsExplicitlyEnabled(IReadOnlyDictionary<string, bool> settings, string key) =>
        settings.TryGetValue(key, out var enabled) && enabled;
}
