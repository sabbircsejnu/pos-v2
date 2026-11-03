using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.UnitTests.HR;

public sealed class HrAttendanceProcessingRulesTests
{
    [Fact]
    public void CrossMidnightPairUsesShiftStartWorkDateAndDeductsUnpaidBreakOnce()
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(
            new(date, Shift() with
            {
                StartTimeLocal = new(10, 0),
                EndTimeLocal = new(2, 0),
                CrossesMidnight = true,
                MinimumWorkMinutes = 900,
                MaximumAllowedWorkMinutes = 960,
                Breaks = [new(1, 30, false)]
            }, "NormalWorkingDay"),
            [
                Punch(1, date, 10, "IN"),
                Punch(2, date.AddDays(1), 2, "OUT")
            ]);

        Assert.Equal(new DateTime(2026, 10, 10, 10, 0, 0), result.FirstInAtLocal);
        Assert.Equal(new DateTime(2026, 10, 11, 2, 0, 0), result.LastOutAtLocal);
        Assert.Equal(930, result.WorkedMinutes);
        Assert.Equal(30, result.BreakMinutes);
        Assert.Equal("Present", result.Status);
        Assert.Equal([1L, 2L], result.PunchIds);
    }

    [Fact]
    public void LateGraceAndEarlyLeaveAreMeasuredWithinTheShiftPunchWindow()
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(new(date,
            Shift() with { GraceInMinutes = 5, GraceOutMinutes = 5 }, "NormalWorkingDay"),
            [Punch(1, date, 9, "IN"), Punch(2, date, 16, "OUT")]);

        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(55, result.EarlyLeaveMinutes);
        Assert.Equal("HalfDay", result.Status);
    }

    [Theory]
    [InlineData("NormalWorkingDay", "Absent")]
    [InlineData("Holiday", "Holiday")]
    [InlineData("WeeklyOff", "WeeklyOff")]
    public void NoPunchesRetainThePublishedDayClassification(string classification, string expected)
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(new(date, Shift(), classification), []);
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.PunchIds);
    }

    [Fact]
    public void OnePunchIsMissingPunchAndCannotCreateWorkedTime()
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(new(date, Shift(), "NormalWorkingDay"),
            [Punch(1, date, 9, "IN")]);
        Assert.Equal("MissingPunch", result.Status);
        Assert.Equal(0, result.WorkedMinutes);
        Assert.Null(result.LastOutAtLocal);
    }

    [Fact]
    public void TrailingInPunchMakesMultiPunchPairMissingRatherThanFalsePresent()
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(new(date, Shift(), "NormalWorkingDay"),
            [Punch(1, date, 9, "IN"), Punch(2, date, 12, "OUT"), Punch(3, date, 13, "IN")]);
        Assert.Equal("MissingPunch", result.Status);
        Assert.Null(result.LastOutAtLocal);
        Assert.Equal([1L, 2L, 3L], result.PunchIds);
    }

    [Fact]
    public void PunchesFromDifferentTimeZonesAreRejectedInsteadOfPairedByWallClock()
    {
        var date = new DateOnly(2026, 10, 10);
        var first = Punch(1, date, 9, "IN");
        var second = Punch(2, date, 17, "OUT") with { TimeZoneId = "America/New_York" };
        Assert.Throws<InvalidOperationException>(() => HrAttendanceProcessingRules.Calculate(
            new(date, Shift(), "NormalWorkingDay"), [first, second]));
    }

    [Fact]
    public void PairExceedingMaximumWorkDurationIsNotAccepted()
    {
        var date = new DateOnly(2026, 10, 10);
        var result = HrAttendanceProcessingRules.Calculate(new(date,
            Shift() with
            {
                MaximumAllowedWorkMinutes = 480,
                AllowedEarlyPunchMinutes = 15,
                AllowedLatePunchMinutes = 30
            }, "NormalWorkingDay"),
            [
                new(1, date.ToDateTime(new(8, 45)), DateTimeOffset.UtcNow, "UTC", "IN"),
                new(2, date.ToDateTime(new(17, 30)), DateTimeOffset.UtcNow, "UTC", "OUT")
            ]);
        Assert.Equal("MissingPunch", result.Status);
        Assert.Null(result.LastOutAtLocal);
        Assert.Equal(0, result.WorkedMinutes);
    }

    private static HrShiftData Shift() => new("DAY", "Day", new(9, 0), new(17, 0),
        false, 480, 480, 0, 0, 600, 0, 0, false, []);

    private static HrAttendancePunchSample Punch(long id, DateOnly date, int hour, string direction) =>
        new(id, date.ToDateTime(new TimeOnly(hour, 0)), DateTimeOffset.UtcNow, "UTC", direction);
}
