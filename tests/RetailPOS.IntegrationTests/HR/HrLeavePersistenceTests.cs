using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Infrastructure.HR.Leave;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrLeavePersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext db = null!;
    private HrLeaveStore store = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Leave" });
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent"
        });
        db.HrEmployees.Add(new HrEmployee
        {
            Id = 1, BusinessId = 1, EmployeeCode = "E-1", FirstName = "A",
            DisplayName = "A", CurrentEmploymentTypeId = 1, JoiningDate = new DateOnly(2025, 6, 15),
            CurrentPrimaryLocationProjectionId = 10
        });
        db.OperationalLocationProjections.Add(new OperationalLocationProjection
        {
            Id = 10, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
            SourceEntityId = "10", Name = "Outlet", SourceVersion = 1
        });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 10,
            LocationNameSnapshot = "Outlet", SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "10",
            LocationSourceVersionSnapshot = 1, EffectiveFrom = new DateOnly(2025, 1, 1),
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
        });
        db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 1, EmployeeId = 1, EmploymentTypeId = 1,
            StartDate = new DateOnly(2025, 6, 15)
        });
        await db.SaveChangesAsync();
        store = new HrLeaveStore(db);
    }

    [Fact]
    public async Task PoliciesAndLedgerAreBusinessScopedVersionedAndIdempotent()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        var policy = await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);
        var context = new HrLeavePolicyContext(
            await db.HrLeavePolicyVersions.SingleAsync(row => row.Id == policy.Id),
            new DateOnly(2025, 6, 15), null, null);
        var write = Write("opening-1", 5m);

        var first = await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [write], 101, default);
        var replay = await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [write], 101, default);
        var balance = Assert.Single(await store.GetBalancesAsync(1, 1, type.Id, default));

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Entries.Single().Id, replay.Entries.Single().Id);
        Assert.Equal(1, first.Entries.Single().Id);
        Assert.Equal(5m, balance.Opening);
        Assert.Equal(5m, balance.Remaining);
        Assert.Equal(2, balance.Version);
        Assert.Empty(await store.GetTypesAsync(2, default));
        Assert.Empty(await store.GetPoliciesAsync(2, null, default));
        Assert.Empty(await store.GetBalancesAsync(2, 1, null, default));
        var entitlement = await db.HrLeaveEntitlements.SingleAsync();
        entitlement.JoiningDateSnapshot = entitlement.JoiningDateSnapshot.AddDays(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(entitlement).ReloadAsync();
    }

    [Fact]
    public async Task IdempotencyPayloadConflictAndImmutableEvidenceAreRejected()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        var policy = await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);
        var context = new HrLeavePolicyContext(
            await db.HrLeavePolicyVersions.SingleAsync(row => row.Id == policy.Id),
            new DateOnly(2025, 6, 15), null, null);
        var write = Write("opening-1", 5m);
        var posted = await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [write], 101, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PostLedgerEntriesAsync(
            1, 1, type.Id, context, [Write("opening-1", 6m)], 101, default));
        var ledgerEntry = await db.HrLeaveLedgerEntries.SingleAsync(row => row.Id == posted.Entries[0].Id);
        ledgerEntry.Reason = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LeaveYearClosePostsCarryForwardExpiryAndReplaysIdempotently()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        var policy = Policy(type.Id) with
        {
            CarryForwardAllowed = true,
            CarryForwardCap = 3m,
            CarryForwardExpiryMonths = 6
        };
        await store.CreatePolicyVersionAsync(1, policy, default);
        var leaveYearStart = new DateOnly(2025, 1, 1);
        var nextYearStart = new DateOnly(2026, 1, 1);
        var expiryDate = new DateOnly(2026, 7, 1);
        var context = await store.ResolvePolicyAsync(1, 1, type.Id, leaveYearStart, default);
        Assert.Equal(6, context.Policy.CarryForwardExpiryMonths);
        var opening = Write("close-opening", 10m) with { EffectiveDate = leaveYearStart };
        await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [opening], 101, default);

        var balance = Assert.Single(await store.GetBalancesAsync(1, 1, type.Id, default));
        var review = await store.ReviewLeaveYearCloseAsync(
            1, 1, type.Id, leaveYearStart, balance.Version,
            "close-review-1", 101, default);
        Assert.Equal(expiryDate, review.CarryForwardExpiresOn);
        var closed = await store.CloseLeaveYearAsync(
            1, 1, type.Id, leaveYearStart, review.Version,
            "close-1", 101, default);
        var replay = await store.CloseLeaveYearAsync(
            1, 1, type.Id, leaveYearStart, review.Version,
            "close-1", 101, default);

        Assert.Equal("Closed", closed.Status);
        Assert.Equal(16m, closed.BalanceBeforeClose);
        Assert.Equal(3m, closed.CarryForwardAmount);
        Assert.Equal(13m, closed.ExpiredAmount);
        Assert.Equal(expiryDate, closed.CarryForwardExpiresOn);
        Assert.True(replay.IsReplay);
        Assert.Equal(closed.Version, replay.Version);

        var current = await store.GetBalancesAsync(1, 1, type.Id, default);
        Assert.Equal(3m, current.Single(row => row.LeaveYearStart == leaveYearStart).Remaining);
        var nextYear = current.Single(row => row.LeaveYearStart == nextYearStart);
        Assert.Equal(3m, nextYear.CarriedForward);
        Assert.Equal(expiryDate, nextYear.CarryForwardExpiresOn);
        Assert.Single(await db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.TransactionType == HrLeaveLedgerTransactionType.CarryForward).ToListAsync());

        var reopened = await store.ReopenLeaveYearAsync(
            1, 1, type.Id, leaveYearStart, closed.Version,
            "reopen-1", "Approved correction", 101, default);
        Assert.Equal("Open", reopened.Status);
        var adjustment = new HrLeaveLedgerWrite(
            HrLeaveLedgerTransactionType.Adjustment, 1m, new DateOnly(2025, 2, 1),
            "ManualAdjustment", "close-adjustment", "close-adjustment",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("Adjustment|1|2025-02-01"))),
            "Approved leave-year close correction");
        await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [adjustment], 101, default);
        var reopenedBalance = Assert.Single(await store.GetBalancesAsync(1, 1, type.Id, default),
            row => row.LeaveYearStart == leaveYearStart);
        var correctedReview = await store.ReviewLeaveYearCloseAsync(
            1, 1, type.Id, leaveYearStart, reopenedBalance.Version,
            "close-review-2", 101, default);
        var correctedClose = await store.CloseLeaveYearAsync(
            1, 1, type.Id, leaveYearStart, correctedReview.Version,
            "close-2", 101, default);
        Assert.Equal(17m, correctedClose.BalanceBeforeClose);
        Assert.Equal(14m, correctedClose.ExpiredAmount);
        Assert.Equal(2, correctedClose.CloseVersion);
        Assert.Single(await db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.TransactionType == HrLeaveLedgerTransactionType.CarryForward).ToListAsync());

        var nextYearContext = await store.ResolvePolicyAsync(
            1, 1, type.Id, expiryDate, default);
        var expiry = await store.ExpireCarryForwardBalancesAsync(
            1, 1, type.Id, expiryDate, nextYearContext, 101, default);
        var expiryReplay = await store.ExpireCarryForwardBalancesAsync(
            1, 1, type.Id, expiryDate, nextYearContext, 101, default);
        Assert.Single(expiry.Entries);
        Assert.Empty(expiryReplay.Entries);
        var expiredNextYear = (await store.GetBalancesAsync(1, 1, type.Id, default))
            .Single(row => row.LeaveYearStart == nextYearStart);
        Assert.Equal(0m, expiredNextYear.Remaining);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PostLedgerEntriesAsync(
            1, 1, type.Id, context,
            [Write("closed-year-write", 1m) with { EffectiveDate = new DateOnly(2025, 2, 1) }],
            101, default));
    }

    [Fact]
    public async Task LeaveYearCannotEnterClosingReviewBeforeYearEnd()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var leaveYearStart = new DateOnly(today.Year + 1, 1, 1);
        var leaveYearEnd = new DateOnly(today.Year + 1, 12, 31);
        var context = await store.ResolvePolicyAsync(1, 1, type.Id, leaveYearStart, default);
        var opening = Write("future-year-opening", 1m) with { EffectiveDate = leaveYearStart };
        await store.PostLedgerEntriesAsync(1, 1, type.Id, context, [opening], 101, default);
        var balance = Assert.Single(await store.GetBalancesAsync(1, 1, type.Id, default));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReviewLeaveYearCloseAsync(
            1, 1, type.Id, leaveYearStart, balance.Version, "premature-review", 101, default));

        Assert.Empty(await db.HrLeaveYearLifecycleEvents.ToListAsync());
        Assert.Equal("Open", Assert.Single(await store.GetBalancesAsync(1, 1, type.Id, default)).Status);
        Assert.True(leaveYearEnd > today);
    }

    [Fact]
    public async Task OverlappingPolicySuccessorIsRejectedWithoutChangingHistory()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);

        var overlap = Policy(type.Id) with
        {
            VersionNo = 2,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreatePolicyVersionAsync(1, overlap, default));
        Assert.Single(await db.HrLeavePolicyVersions.ToListAsync());
    }

    [Fact]
    public async Task PolicyVersionsChangeOnlyAtLeaveYearBoundaries()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id) with
        {
            EffectiveTo = new DateOnly(2025, 12, 31)
        }, default);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreatePolicyVersionAsync(1,
            Policy(type.Id) with
            {
                VersionNo = 2,
                EffectiveFrom = new DateOnly(2025, 7, 1)
            }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreatePolicyVersionAsync(1,
            Policy(type.Id) with
            {
                VersionNo = 2,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2026, 6, 30)
            }, default));

        var successor = await store.CreatePolicyVersionAsync(1, Policy(type.Id) with
        {
            VersionNo = 2,
            AnnualEntitlement = 24m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        }, default);
        var resolved = await store.ResolvePolicyAsync(
            1, 1, type.Id, new DateOnly(2026, 1, 1), default);

        Assert.Equal(2, successor.VersionNo);
        Assert.Equal(successor.Id, resolved.Policy.Id);
        Assert.Equal(24m, resolved.Policy.AnnualEntitlement);
    }

    [Fact]
    public async Task PolicyVersionsCannotChangeLeaveYearBoundariesWithinScope()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id) with
        {
            EffectiveTo = new DateOnly(2025, 12, 31)
        }, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreatePolicyVersionAsync(1, Policy(type.Id) with
            {
                VersionNo = 2,
                LeaveYearStartMonth = 4,
                LeaveYearStartDay = 1,
                EffectiveFrom = new DateOnly(2026, 4, 1)
            }, default));
    }

    [Fact]
    public async Task PolicyResolutionUsesEmployeeThenLocationThenLocationTypeThenBusiness()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        var business = Policy(type.Id) with { Code = "BUSINESS" };
        var locationType = Policy(type.Id) with
        {
            Code = "LOCATION_TYPE", ScopeType = HrLeavePolicyScopeType.LocationType, ScopeReference = "Outlet"
        };
        var location = Policy(type.Id) with
        {
            Code = "LOCATION", ScopeType = HrLeavePolicyScopeType.Location, ScopeReference = "10"
        };
        var employee = Policy(type.Id) with
        {
            Code = "EMPLOYEE", ScopeType = HrLeavePolicyScopeType.Employee, ScopeReference = "1"
        };
        await store.CreatePolicyVersionAsync(1, business, default);
        await store.CreatePolicyVersionAsync(1, locationType, default);
        await store.CreatePolicyVersionAsync(1, location, default);
        await store.CreatePolicyVersionAsync(1, employee, default);

        var resolved = await store.ResolvePolicyAsync(1, 1, type.Id, new DateOnly(2026, 1, 1), default);

        Assert.Equal("EMPLOYEE", resolved.Policy.Code);
        Assert.Equal(10, resolved.PrimaryLocationProjectionId);
        Assert.Equal("Outlet", resolved.LocationType);
    }

    [Fact]
    public async Task DraftFreezesPublishedRosterContextAndRequestEvidenceCannotBeEdited()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);
        var date = new DateOnly(2026, 1, 10);
        db.Set<HrShift>().Add(new HrShift
        {
            Id = 7, BusinessId = 1, Code = "DAY", Name = "Day",
            StartTimeLocal = new TimeOnly(10, 0), EndTimeLocal = new TimeOnly(18, 0),
            MinimumWorkMinutes = 420, AllowedEarlyPunchMinutes = 15,
            AllowedLatePunchMinutes = 15, GraceInMinutes = 5, GraceOutMinutes = 5
        });
        db.Set<HrRosterAssignment>().Add(new HrRosterAssignment
        {
            Id = 9, BusinessId = 1, EmployeeId = 1, WorkDate = date, ShiftId = 7,
            OperationalLocationProjectionId = 10, DayClassification = "NormalWorkingDay",
            Status = "Published", PublishedByUserId = 101, PublishedAtUtc = DateTime.UtcNow,
            ShiftSnapshotJson = """{"code":"DAY","start":"10:00","end":"18:00"}""",
            LocationSnapshotJson = """{"name":"Outlet"}""",
            CalendarEvidenceJson = """{"WorkDate":"2026-01-10","Classification":"NormalWorkingDay","Source":"PublishedRoster","IsHoliday":false,"IsWeeklyOff":false,"Holidays":[],"WeeklyOffPolicy":null}"""
        });
        await db.SaveChangesAsync();

        var requestStore = new HrLeaveRequestStore(
            db, new HrCalendarStore(db, TimeProvider.System), store, TimeProvider.System);
        var request = await requestStore.CreateDraftAsync(1, 1, 101,
            new HrLeaveRequestDraftInput(type.Id, date, date, "Annual leave"), default);

        var requestDay = Assert.Single(request.Days);
        Assert.Equal("Draft", request.Status);
        Assert.Equal(1m, request.RequestedUnits);
        Assert.Equal(9, requestDay.RosterId);
        Assert.Equal(10, requestDay.OperationalLocationProjectionId);
        Assert.Equal("""{"code":"DAY","start":"10:00","end":"18:00"}""", requestDay.ShiftSnapshotJson);
        Assert.Equal("""{"WorkDate":"2026-01-10","Classification":"NormalWorkingDay","Source":"PublishedRoster","IsHoliday":false,"IsWeeklyOff":false,"Holidays":[],"WeeklyOffPolicy":null}""",
            requestDay.CalendarEvidenceJson);
        Assert.Null(await requestStore.GetAsync(2, request.Id, default));
        Assert.Empty(await requestStore.ListAsync(2, null, null, default));

        var persistedDay = await db.HrLeaveRequestDays.SingleAsync();
        persistedDay.Units = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(persistedDay).Reload();
        Assert.Equal(1m, persistedDay.Units);

        var persistedRequest = await db.HrLeaveRequests.SingleAsync();
        persistedRequest.Reason = "Changed after draft";
        persistedRequest.Version++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(persistedRequest).Reload();

        var documentType = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("MEDICAL", "Medical", true, true), default);
        await store.CreatePolicyVersionAsync(1, Policy(documentType.Id), default);
        var documentRequest = await requestStore.CreateDraftAsync(1, 1, 101,
            new HrLeaveRequestDraftInput(documentType.Id, date, date, "Medical leave"), default);
        var snapshot = await requestStore.FindForApprovalAsync(1, documentRequest.Id, default);
        var submit = new SubmitApprovalCommand(
            ApprovalProcessType.Leave, HrLeaveApprovalConstants.TargetType,
            documentRequest.Id.ToString(), ApprovalScopeType.Location, "10",
            documentRequest.Version, snapshot!.RequestHash!);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            requestStore.PrepareSubmissionAsync(1, 101, submit, default));
    }

    [Fact]
    public async Task DraftCannotCrossPrimaryLocationTransfer()
    {
        var type = await store.CreateTypeAsync(1,
            new HrLeaveTypeInput("ANNUAL", "Annual", true, false), default);
        await store.CreatePolicyVersionAsync(1, Policy(type.Id), default);
        var firstDate = new DateOnly(2026, 1, 10);
        var secondDate = firstDate.AddDays(1);
        db.OperationalLocationProjections.Add(new OperationalLocationProjection
        {
            Id = 11, BusinessId = 1, SourceSystem = "RetailPOS",
            SourceEntityType = "Outlet", SourceEntityId = "11", Name = "New Outlet", SourceVersion = 1
        });
        var originalAssignment = await db.HrEmployeeLocationAssignments.SingleAsync();
        originalAssignment.EffectiveTo = firstDate;
        originalAssignment.Status = "Closed";
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 11,
            LocationNameSnapshot = "New Outlet", SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "11",
            LocationSourceVersionSnapshot = 1, EffectiveFrom = secondDate, Status = "Effective",
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
        });
        db.Set<HrShift>().Add(new HrShift
        {
            Id = 7, BusinessId = 1, Code = "DAY", Name = "Day",
            StartTimeLocal = new TimeOnly(10, 0), EndTimeLocal = new TimeOnly(18, 0),
            MinimumWorkMinutes = 420, AllowedEarlyPunchMinutes = 15,
            AllowedLatePunchMinutes = 15, GraceInMinutes = 5, GraceOutMinutes = 5
        });
        db.Set<HrRosterAssignment>().Add(new HrRosterAssignment
        {
            Id = 9, BusinessId = 1, EmployeeId = 1, WorkDate = firstDate, ShiftId = 7,
            OperationalLocationProjectionId = 10, DayClassification = "NormalWorkingDay",
            Status = "Published", PublishedByUserId = 101, PublishedAtUtc = DateTime.UtcNow,
            ShiftSnapshotJson = """{"code":"DAY"}""",
            LocationSnapshotJson = """{"name":"Outlet"}""",
            CalendarEvidenceJson = """{"WorkDate":"2026-01-10","Classification":"NormalWorkingDay","Source":"PublishedRoster","IsHoliday":false,"IsWeeklyOff":false,"Holidays":[],"WeeklyOffPolicy":null}"""
        });
        await db.SaveChangesAsync();

        var requestStore = new HrLeaveRequestStore(
            db, new HrCalendarStore(db, TimeProvider.System), store, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => requestStore.CreateDraftAsync(
            1, 1, 101, new HrLeaveRequestDraftInput(type.Id, firstDate, secondDate, "Transfer test"), default));
        Assert.Empty(await db.HrLeaveRequests.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static HrLeavePolicyInput Policy(long leaveTypeId) =>
        new(leaveTypeId, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1,
            HrLeaveAccrualMethod.MonthlyOnJoiningDateAnniversary, 12m, false, null, null, null,
            false, null, 1, 1, new DateOnly(2025, 1, 1), null, true);

    private static HrLeaveLedgerWrite Write(string key, decimal quantity)
    {
        var canonical = $"{key}|Opening|{quantity}|2026-01-01";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical)));
        return new(HrLeaveLedgerTransactionType.Opening, quantity, new DateOnly(2026, 1, 1),
            "Manual", key, key, hash, "Opening balance");
    }
}
