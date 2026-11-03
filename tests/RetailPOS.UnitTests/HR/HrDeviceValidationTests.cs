using RetailPOS.Application.HR.Attendance;

namespace RetailPOS.UnitTests.HR;

public sealed class HrDeviceValidationTests
{
    internal static HrDeviceData Device() => new("D1", "Serial", "Vendor", "Terminal", 10, "ADMS", "Asia/Dhaka");
    [Theory]
    [InlineData("Active")] [InlineData("Offline")] [InlineData("Disabled")] [InlineData("Error")]
    public void SafeMetadataAndExplicitTimezoneAccepted(string status) =>
        HrDeviceHandlers.Validate(Device() with { Status = status, ConnectionHost = "terminal.internal", ConnectionPort = 443,
            CredentialSecretReference = "device-secret-01" });
    [Theory]
    [InlineData("unknown-zone")] [InlineData("uri-host")] [InlineData("credential-path")]
    [InlineData("port")] [InlineData("host-without-port")] [InlineData("port-without-host")]
    [InlineData("foreign-type")] [InlineData("invalid-status")] [InlineData("control-character")]
    public void InvalidOrPotentiallySecretMetadataRejected(string kind)
    {
        var data = kind switch {
            "unknown-zone" => Device() with { DeviceTimeZoneId = "unknown-timezone" },
            "uri-host" => Device() with { ConnectionHost = "https://user:password@terminal.internal", ConnectionPort = 443 },
            "credential-path" => Device() with { CredentialSecretReference = @"C:\secrets\device.key" },
            "port" => Device() with { ConnectionHost = "terminal.internal", ConnectionPort = 65536 },
            "host-without-port" => Device() with { ConnectionHost = "terminal.internal" },
            "port-without-host" => Device() with { ConnectionPort = 443 },
            "foreign-type" => Device() with { ProtocolType = "Unknown" },
            "invalid-status" => Device() with { Status = "Ready" },
            _ => Device() with { SerialNumber = "serial\nsecret" }
        };
        Assert.Throws<ArgumentException>(() => HrDeviceHandlers.Validate(data));
    }
}
