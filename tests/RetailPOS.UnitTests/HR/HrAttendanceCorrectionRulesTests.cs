using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;

namespace RetailPOS.UnitTests.HR;

public sealed class HrAttendanceCorrectionRulesTests
{
    private static readonly DateOnly WorkDate = new(2026, 10, 10);
    private static readonly HrShiftData NightShift = new("NIGHT", "Night",
        new TimeOnly(10, 0), new TimeOnly(2, 0), true, 900, 960, 30, 30,
        600, 5, 5, false, [new(1, 30, false)]);
    private static readonly HrAttendanceCorrectionValues Valid = new(
        new DateTime(2026, 10, 10, 10, 0, 0),
        new DateTime(2026, 10, 11, 2, 0, 0),
        930, 30, 0, 0, "Present");

    [Fact]
    public void CompleteCrossMidnightCorrectionUsesOneShiftStartDate()
    {
        HrAttendanceCorrectionRules.ValidateShape(Guid.NewGuid(), 1, 1, Valid, "Corrected from source records");
        HrAttendanceCorrectionRules.ValidateAgainstShift(WorkDate, NightShift, Valid);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void NegativeMinutesAreRejected(int workedMinutes, int breakMinutes)
    {
        Assert.Throws<ArgumentException>(() => HrAttendanceCorrectionRules.ValidateShape(
            Guid.NewGuid(), 1, 1, Valid with { WorkedMinutes = workedMinutes, BreakMinutes = breakMinutes }, "reason"));
    }

    [Fact]
    public void PunchOutsideFrozenWindowIsRejected()
    {
        var tooEarly = Valid with { FirstInAtLocal = new DateTime(2026, 10, 10, 8, 0, 0) };
        Assert.Throws<ArgumentException>(() =>
            HrAttendanceCorrectionRules.ValidateAgainstShift(WorkDate, NightShift, tooEarly));
    }

    [Fact]
    public void InvalidPairDurationAndBreakTotalsAreRejected()
    {
        var reversed = Valid with { LastOutAtLocal = Valid.FirstInAtLocal!.Value.AddMinutes(-1) };
        Assert.Throws<ArgumentException>(() =>
            HrAttendanceCorrectionRules.ValidateAgainstShift(WorkDate, NightShift, reversed));

        var excessiveBreak = Valid with { BreakMinutes = 961 };
        Assert.Throws<ArgumentException>(() =>
            HrAttendanceCorrectionRules.ValidateAgainstShift(WorkDate, NightShift, excessiveBreak));
    }

    [Fact]
    public void PartialPunchMustRemainMissingPunchWithNoWorkedMinutes()
    {
        var onePunch = Valid with { LastOutAtLocal = null, WorkedMinutes = 0, BreakMinutes = 0, Status = "MissingPunch" };
        HrAttendanceCorrectionRules.ValidateShape(Guid.NewGuid(), 1, 1, onePunch, "manual review");
        Assert.Throws<ArgumentException>(() => HrAttendanceCorrectionRules.ValidateShape(
            Guid.NewGuid(), 1, 1, onePunch with { Status = "Present" }, "manual review"));
    }
}
