using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;

namespace RetailPOS.UnitTests.HR;

public sealed class HrRosterRulesTests
{
    [Fact]
    public void WorkDateMustPermitNextCalendarDateResolution() =>
        Assert.Throws<ArgumentException>(() => HrRosterRules.Validate(DateOnly.MaxValue, 1, 2, 3, "Manual", "Normal", "NormalWorkingDay"));
    [Theory]
    [InlineData("Normal")] [InlineData("Extended")] [InlineData("Special")]
    public void WorkKindsKeepOneStartDate(string kind)
    {
        var date = new DateOnly(2027, 1, 1);
        HrRosterRules.Validate(date, 1, 2, 3, "Manual", kind, "NormalWorkingDay");
        var window = HrShiftRules.Window(date, new(10, 0), new(2, 0), true);
        Assert.Equal(date, DateOnly.FromDateTime(window.StartLocal));
        Assert.Equal(date.AddDays(1), DateOnly.FromDateTime(window.EndLocal));
        Assert.Equal(960, (window.EndLocal - window.StartLocal).TotalMinutes);
    }
    [Theory]
    [InlineData(0, 2, 3, "Manual", "Normal", "NormalWorkingDay")]
    [InlineData(1, 0, 3, "Manual", "Normal", "NormalWorkingDay")]
    [InlineData(1, 2, 0, "Manual", "Normal", "NormalWorkingDay")]
    [InlineData(1, 2, 3, "Invalid", "Normal", "NormalWorkingDay")]
    [InlineData(1, 2, 3, "Manual", "Invalid", "NormalWorkingDay")]
    [InlineData(1, 2, 3, "Manual", "Normal", "Absent")]
    public void InvalidAssignmentsRejected(long employee, long shift, long location, string source, string kind, string day) =>
        Assert.Throws<ArgumentException>(() => HrRosterRules.Validate(new(2027, 1, 1), employee, shift, location, source, kind, day));
}
