using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities.HR.Leave;

namespace RetailPOS.UnitTests.HR;

public sealed class HrLeaveRulesTests
{
    [Fact]
    public async Task PolicyRequiresAnExplicitSupportedAccrualMethod()
    {
        var input = new HrLeavePolicyInput(
            1, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1, null,
            12m, false, null, null, null, false, null, 1, 1,
            new DateOnly(2026, 1, 1), null, true);
        var handler = new CreateHrLeavePolicyVersionHandler(
            Mock.Of<IHrLeaveAccess>(), Mock.Of<IHrLeaveStore>(), Mock.Of<IRequiredAuditTransaction>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateHrLeavePolicyVersionCommand(input), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateHrLeavePolicyVersionCommand(
                input with { AccrualMethod = (HrLeaveAccrualMethod)999 }), default));
    }

    [Fact]
    public void LeaveYearUsesPolicyStartAndReturnsInclusiveEnd()
    {
        var beforeStart = HrLeaveRules.GetLeaveYear(new DateOnly(2026, 3, 31), 4, 1);
        var onStart = HrLeaveRules.GetLeaveYear(new DateOnly(2026, 4, 1), 4, 1);

        Assert.Equal(new DateOnly(2025, 4, 1), beforeStart.Start);
        Assert.Equal(new DateOnly(2026, 3, 31), beforeStart.End);
        Assert.Equal(new DateOnly(2026, 4, 1), onStart.Start);
        Assert.Equal(new DateOnly(2027, 3, 31), onStart.End);
    }

    [Fact]
    public void RequestDaysUseOnlyPublishedWorkdayEvidence()
    {
        Assert.Equal(1m, HrLeaveRequestRules.UnitsForDay("NormalWorkingDay", hasPublishedRoster: true));
        Assert.Equal(0m, HrLeaveRequestRules.UnitsForDay("Holiday", hasPublishedRoster: false));
        Assert.Equal(0m, HrLeaveRequestRules.UnitsForDay("WeeklyOff", hasPublishedRoster: false));
        Assert.Throws<InvalidOperationException>(() =>
            HrLeaveRequestRules.UnitsForDay("NormalWorkingDay", hasPublishedRoster: false));
        Assert.Throws<ArgumentException>(() =>
            HrLeaveRequestRules.UnitsForDay("Unknown", hasPublishedRoster: true));
    }

    [Fact]
    public void RequestRangeMustBeValidAndAtMost367InclusiveDays()
    {
        var first = new DateOnly(2026, 1, 1);
        HrLeaveRequestRules.ValidateDraft(new(1, first, first.AddDays(366), "Reason"));

        Assert.Throws<ArgumentException>(() =>
            HrLeaveRequestRules.ValidateDraft(new(1, first, first.AddDays(367), "Reason")));
        Assert.Throws<ArgumentException>(() =>
            HrLeaveRequestRules.ValidateDraft(new(1, first.AddDays(1), first, "Reason")));
        Assert.Throws<ArgumentException>(() =>
            HrLeaveRequestRules.ValidateDraft(new(1, first, first, " ")));
    }

    [Fact]
    public void LeaveYearCloseCapsPositiveBalanceAndExpiresOnExactMonthAnniversary()
    {
        var policy = new HrLeavePolicyVersion
        {
            CarryForwardAllowed = true,
            CarryForwardCap = 4m,
            CarryForwardExpiryMonths = 6
        };

        var result = HrLeaveYearCloseRules.Calculate(10m, policy, new DateOnly(2026, 12, 31));

        Assert.Equal(10m, result.BalanceBeforeClose);
        Assert.Equal(4m, result.CarryForwardAmount);
        Assert.Equal(6m, result.ExpiredAmount);
        Assert.Equal(new DateOnly(2027, 7, 1), result.CarryForwardExpiresOn);
    }

    [Fact]
    public void LeaveYearCloseDoesNotCarryOrExpireDisabledOrNegativeBalance()
    {
        var disabledPolicy = new HrLeavePolicyVersion { CarryForwardAllowed = false };
        var disabled = HrLeaveYearCloseRules.Calculate(5m, disabledPolicy, new DateOnly(2026, 12, 31));
        var negative = HrLeaveYearCloseRules.Calculate(
            -2m, new HrLeavePolicyVersion
            {
                CarryForwardAllowed = true,
                CarryForwardCap = 3m,
                CarryForwardExpiryMonths = 6
            },
            new DateOnly(2026, 12, 31));

        Assert.Equal(0m, disabled.CarryForwardAmount);
        Assert.Equal(5m, disabled.ExpiredAmount);
        Assert.Equal(-2m, negative.BalanceBeforeClose);
        Assert.Equal(0m, negative.CarryForwardAmount);
        Assert.Equal(0m, negative.ExpiredAmount);
        Assert.Null(negative.CarryForwardExpiresOn);
    }

    [Fact]
    public async Task LeaveYearPolicyContextIsAnchoredToItsStartDate()
    {
        var store = new Mock<IHrLeaveStore>();
        var current = new HrLeavePolicyContext(new HrLeavePolicyVersion
        {
            Id = 2, LeaveYearStartMonth = 1, LeaveYearStartDay = 1
        }, new DateOnly(2020, 1, 1), 20, "Outlet");
        var atYearStart = new HrLeavePolicyContext(new HrLeavePolicyVersion
        {
            Id = 1, LeaveYearStartMonth = 1, LeaveYearStartDay = 1
        }, new DateOnly(2020, 1, 1), 10, "Outlet");
        store.Setup(value => value.ResolvePolicyAsync(1, 3, 4, new DateOnly(2026, 6, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        store.Setup(value => value.ResolvePolicyAsync(1, 3, 4, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(atYearStart);

        var resolved = await HrLeaveRules.ResolvePolicyForLeaveYearAsync(
            store.Object, 1, 3, 4, new DateOnly(2026, 6, 15), default);

        Assert.Same(atYearStart, resolved);
    }

    [Fact]
    public void AnnualGrantCreatesOneDueAtLeaveYearStart()
    {
        var dues = HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart,
            18m,
            new DateOnly(2024, 5, 10),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 6, 1));

        Assert.Equal([new HrLeaveAccrualDue(new DateOnly(2026, 1, 1), 18m)], dues);
    }

    [Fact]
    public void MonthlyAccrualFollowsJoiningDateAnniversary()
    {
        var dues = HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.MonthlyOnJoiningDateAnniversary,
            12m,
            new DateOnly(2025, 12, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 3, 15));

        Assert.Equal(
        [
            new HrLeaveAccrualDue(new DateOnly(2026, 1, 15), 1m),
            new HrLeaveAccrualDue(new DateOnly(2026, 2, 15), 1m),
            new HrLeaveAccrualDue(new DateOnly(2026, 3, 15), 1m)
        ], dues);
    }

    [Fact]
    public void MonthlyAndAnnualAccrualClampLeapDayAnniversaries()
    {
        var monthly = HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.MonthlyOnJoiningDateAnniversary,
            12m,
            new DateOnly(2024, 1, 31),
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new DateOnly(2024, 3, 31));
        var annual = HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.AnnualOnJoiningDateAnniversary,
            18m,
            new DateOnly(2024, 2, 29),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new DateOnly(2025, 3, 1));

        Assert.Equal(new DateOnly(2024, 2, 29), monthly[0].DueDate);
        Assert.Equal(new DateOnly(2024, 3, 31), monthly[1].DueDate);
        Assert.Equal(new HrLeaveAccrualDue(new DateOnly(2025, 2, 28), 18m), Assert.Single(annual));
    }

    [Fact]
    public void AccrualThroughDateIsInclusiveAndCannotCrossLeaveYear()
    {
        var dueToday = HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.AnnualOnJoiningDateAnniversary,
            10m,
            new DateOnly(2020, 5, 10),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 5, 10));

        Assert.Equal(new HrLeaveAccrualDue(new DateOnly(2026, 5, 10), 10m), Assert.Single(dueToday));
        Assert.Throws<ArgumentOutOfRangeException>(() => HrLeaveRules.GetAccrualDues(
            HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart,
            10m,
            new DateOnly(2020, 5, 10),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2027, 1, 1)));
    }
}
