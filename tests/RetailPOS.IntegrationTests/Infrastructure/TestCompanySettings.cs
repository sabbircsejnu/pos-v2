using Moq;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Tests.Infrastructure;

internal static class TestCompanySettings
{
    public static ICompanyTimeZoneProvider Provider(string timeZone = "Asia/Dhaka")
    {
        var provider = new Mock<ICompanyTimeZoneProvider>();
        provider.Setup(port => port.GetTimeZoneIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(timeZone);
        return provider.Object;
    }
}
