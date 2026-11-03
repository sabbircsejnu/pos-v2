namespace RetailPOS.Application.HR.Capabilities;

public static class HrPayrollCapabilityKeys
{
    public const string Hr = "hr.enabled";
    public const string Payroll = "payroll.enabled";
    public const string Attendance = "attendance.enabled";
    public const string DeviceIntegration = "deviceintegration.enabled";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { Hr, Payroll, Attendance, DeviceIntegration });
}

public sealed record HrPayrollCapabilities(
    long BusinessId,
    bool HrEnabled,
    bool PayrollEnabled,
    bool AttendanceEnabled,
    bool DeviceIntegrationEnabled)
{
    public bool IsEnabled(string capability) => capability switch
    {
        HrPayrollCapabilityKeys.Hr => HrEnabled,
        HrPayrollCapabilityKeys.Payroll => PayrollEnabled,
        HrPayrollCapabilityKeys.Attendance => AttendanceEnabled,
        HrPayrollCapabilityKeys.DeviceIntegration => DeviceIntegrationEnabled,
        _ => throw new ArgumentException("Unknown HR/Payroll capability.", nameof(capability))
    };
}

public interface IHrPayrollCapabilityScope
{
    long RequireBusinessId();
}

public interface IHrPayrollCapabilityReadStore
{
    Task<IReadOnlyDictionary<string, bool>> GetExplicitSettingsAsync(
        long businessId, CancellationToken cancellationToken);
}

public interface IHrPayrollCapabilityService
{
    Task<HrPayrollCapabilities> GetAsync(CancellationToken cancellationToken);
    Task EnsureEnabledAsync(string capability, CancellationToken cancellationToken);
}
