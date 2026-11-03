using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Calendars;

namespace RetailPOS.UnitTests.HR;

public sealed class HrCalendarRulesTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AssignmentPreconditionRejectsInactiveShiftWithoutCreatingRosterLogic(bool active)
    {
        var shift = Shift(); shift.IsActive = active;
        if (active) HrShiftRules.EnsureAssignable(shift);
        else Assert.Throws<InvalidOperationException>(() => HrShiftRules.EnsureAssignable(shift));
    }

    [Theory]
    [InlineData(9, 17, false, 480)]
    [InlineData(10, 2, true, 960)]
    [InlineData(22, 6, true, 480)]
    public void GenericShiftUsesStartDateAndOneWindow(int start, int end, bool midnight, int minutes)
    {
        var shift = Shift(); shift.StartTimeLocal = new(start, 0); shift.EndTimeLocal = new(end, 0);
        shift.CrossesMidnight = midnight;
        HrShiftRules.Validate(shift);
        var date = new DateOnly(2026, 4, 15);
        var window = HrShiftRules.Window(date, shift.StartTimeLocal, shift.EndTimeLocal, midnight);
        Assert.Equal(minutes, (window.EndLocal - window.StartLocal).TotalMinutes);
        Assert.Equal(date, DateOnly.FromDateTime(window.StartLocal));
        Assert.Equal(date.AddDays(midnight ? 1 : 0), DateOnly.FromDateTime(window.EndLocal));
    }

    [Theory]
    [InlineData("zero")] [InlineData("negative")] [InlineData("falseMidnight")]
    [InlineData("precision")] [InlineData("max")] [InlineData("minimum")]
    [InlineData("early")] [InlineData("late")] [InlineData("ambiguous")]
    [InlineData("maxWindow")] [InlineData("rest")] [InlineData("negativeGraceOut")]
    [InlineData("grace")] [InlineData("zeroBreak")] [InlineData("longBreak")]
    [InlineData("netWork")] [InlineData("duplicateSequence")]
    public void InvalidShiftSettingsRejected(string scenario)
    {
        var s = Shift();
        switch (scenario)
        {
            case "zero": s.EndTimeLocal = s.StartTimeLocal; break;
            case "negative": s.EndTimeLocal = new(2, 0); break;
            case "falseMidnight": s.CrossesMidnight = true; break;
            case "precision": s.StartTimeLocal = new(9, 0, 1); break;
            case "max": s.MaximumAllowedWorkMinutes = 400; break;
            case "minimum": s.MinimumWorkMinutes = 481; break;
            case "early": s.AllowedEarlyPunchMinutes = -1; break;
            case "late": s.AllowedLatePunchMinutes = -1; break;
            case "ambiguous": s.AllowedEarlyPunchMinutes = 960; break;
            case "maxWindow": s.MaximumAllowedWorkMinutes = 1400; s.AllowedLatePunchMinutes = 40; break;
            case "rest": s.MinimumRestBeforeNextShiftMinutes = -1; break;
            case "negativeGraceOut": s.GraceOutMinutes = -1; break;
            case "grace": s.GraceInMinutes = 480; break;
            case "zeroBreak": s.Breaks.Add(new() { Sequence = 1 }); break;
            case "longBreak": s.Breaks.Add(new() { Sequence = 1, DurationMinutes = 480 }); break;
            case "netWork": s.Breaks.Add(new() { Sequence = 1, DurationMinutes = 61 }); break;
            case "duplicateSequence":
                s.Breaks.AddRange([new() { Sequence = 1, DurationMinutes = 10 }, new() { Sequence = 1, DurationMinutes = 10 }]); break;
        }
        Assert.Throws<ArgumentException>(() => HrShiftRules.Validate(s));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PaidAndUnpaidBreaksRetainTreatment(bool paid)
    {
        var s = Shift(); s.Breaks.Add(new() { Sequence = 1, DurationMinutes = 60, IsPaid = paid });
        HrShiftRules.Validate(s); Assert.Equal(paid, s.Breaks[0].IsPaid);
    }

    [Theory]
    [InlineData(false, false, "Holiday")]
    [InlineData(false, true, "NormalWorkingDay")]
    [InlineData(true, true, "WeeklyOff")]
    public void ExplicitPrecedencePreservesHolidayOverlapEvidence(bool employee, bool roster, string expected)
    {
        var date = new DateOnly(2026, 4, 17);
        var holiday = new HrHolidayResponse(1, 1, new(date, "H", "Holiday", "Public", true, null), true, 1);
        var policy = new HrWeeklyOffResponse(1, 1, 1, new(new("Company"), date, null, [date.DayOfWeek]), true, 1);
        var result = HrDayClassification.Resolve(date, [holiday], policy,
            employee ? new("WeeklyOff", true) : null, roster ? new("NormalWorkingDay", false) : null);
        Assert.Equal(expected, result.Classification); Assert.True(result.IsHoliday); Assert.Single(result.Holidays);
        Assert.Equal(employee || !roster, result.IsWeeklyOff); Assert.Same(policy, result.WeeklyOffPolicy);
    }

    [Theory]
    [InlineData("Company", 1)] [InlineData("LocationType", 2)] [InlineData("Location", 3)]
    public void ScopePrecedenceIsExplicit(string scope, int priority) =>
        Assert.Equal(priority, HrDayClassification.Priority(new(scope)));

    [Fact]
    public async Task ScopedActorCannotManageBusinessWideConfigurationEvenWithManagePermission()
    {
        var access = new Mock<IHrOrganisationAccess>();
        access.Setup(a => a.RequireBusinessId()).Returns(1);
        access.Setup(a => a.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, []));
        var store = new Mock<IHrCalendarStore>();
        var handlers = new HrCalendarHandlers(access.Object, store.Object, Mock.Of<IRequiredAuditTransaction>(), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handlers.Handle(
            new SaveHrShiftCommand(null, null, Data()), default));
        store.VerifyNoOtherCalls();
    }

    private static HrShift Shift() => new()
    {
        StartTimeLocal = new(9, 0), EndTimeLocal = new(17, 0), MinimumWorkMinutes = 420
    };
    private static HrShiftData Data() => new("DAY", "Day", new(9, 0), new(17, 0), false, 420, null, 0, 0, 660, 5, 5, true, []);
}
