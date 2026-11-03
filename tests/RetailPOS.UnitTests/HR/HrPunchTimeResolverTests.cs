using RetailPOS.Application.HR.Attendance;

namespace RetailPOS.UnitTests.HR;

public sealed class HrPunchTimeResolverTests
{
    [Fact]
    public void ResolvesLocalTimeUsingConfiguredZoneWithoutChangingWallClockEvidence()
    {
        var local = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Unspecified);
        var resolved = HrPunchTimeResolver.ResolveUtc(local, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 3, 30, 0, TimeSpan.Zero), resolved);
        Assert.Equal(new DateTime(2026, 10, 10, 9, 0, 0), local);
    }

    [Theory]
    [InlineData(2026, 3, 8, 2, 30)]
    [InlineData(2026, 11, 1, 1, 30)]
    public void LeavesAmbiguousOrNonexistentLocalTimeUnresolved(int year, int month, int day, int hour, int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Null(HrPunchTimeResolver.ResolveUtc(new(year, month, day, hour, minute, 0), zone));
    }
}
