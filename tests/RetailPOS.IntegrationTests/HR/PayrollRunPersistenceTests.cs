using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollRunPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext auditContext = new();
    private RetailPOSDbContext db = null!;
    private PayrollRunStore store = null!;
    private PayrollRunCalculationStore calculations = null!;
    private RequiredAuditTransaction audit = null!;
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditChangeTrackerInterceptor(auditContext,
                NullLogger<AuditChangeTrackerInterceptor>.Instance))
            .Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
            new Business { Id = 2, Name = "Other" });
        await db.SaveChangesAsync();
        db.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
        {
            BusinessId = 1,
            VersionNo = 1,
            EffectiveFrom = new(2020, 1, 1),
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101
        });
        await db.SaveChangesAsync();
        await HrRosterPersistenceTests.Seed(db, businessExists: true);
        db.PayrollPeriods.Add(new PayrollPeriod
        {
            BusinessId = 1,
            PeriodCode = "2026-10",
            PeriodStart = new(2026, 10, 1),
            PeriodEnd = new(2026, 10, 31),
            Status = PayrollPeriodStatuses.Processing,
            Version = 2,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow,
            OpenedByUserId = 101,
            ProcessingAtUtc = DateTime.UtcNow,
            ProcessingByUserId = 101
        });
        await db.SaveChangesAsync();
        auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/payroll/v1/periods/1/runs", null, null, null,
            false, null, null, null, null, "actor", "Payroll"));
        audit = new RequiredAuditTransaction(db, auditContext,
            new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);
        store = new PayrollRunStore(db, new PayrollRunValidationStore(db),
            new PayrollRunCalculationStore(db));
        calculations = new PayrollRunCalculationStore(db);
    }

    [Fact]
    public async Task SelectionIsDeterministicForCurrentSalaryFutureSalaryExitAndJoiner()
    {
        await AddSalaryAsync(employeeId: 1, new(2026, 1, 1));
        await AddEmployeeAsync(2, "FUTURE", new(2026, 1, 1), new(2026, 1, 1), null, 10);
        await AddSalaryAsync(employeeId: 2, new(2026, 11, 1));
        await AddEmployeeAsync(3, "EXITED", new(2026, 1, 1), new(2026, 1, 1), new(2026, 9, 30), 10,
            "Exited", "Closed");
        await AddEmployeeAsync(4, "JOINER", new(2026, 10, 15), new(2026, 10, 15), null, 10);
        await AddSalaryAsync(employeeId: 4, new(2026, 10, 15));
        await AddEmployeeAsync(6, "NO-SALARY", new(2026, 1, 1), new(2026, 1, 1), null, 10);
        await AddForeignEmployeeAsync();

        var run = await CreateAsync(new("Business", null, "period-run-1", 2));

        Assert.Equal(2, run.IncludedEmployeeCount);
        Assert.Equal(3, run.ExcludedEmployeeCount);
        Assert.Equal("Draft", run.Status);
        Assert.Equal(1, run.RunNo);
        Assert.Equal("Regular", run.RunType);

        var page = await store.GetEligibilityPageAsync(1, 1, run.Id, 1, 100, default);
        Assert.Equal(5, page.TotalCount);
        Assert.Contains(page.Items, row => row.EmployeeId == 1 && row.IsIncluded);
        Assert.Contains(page.Items, row => row.EmployeeId == 2 && !row.IsIncluded
            && row.ExclusionReasonCode == "SalaryNotYetEffective");
        Assert.Contains(page.Items, row => row.EmployeeId == 3 && !row.IsIncluded
            && row.ExclusionReasonCode == "NoEmploymentOverlap");
        Assert.Contains(page.Items, row => row.EmployeeId == 4 && row.IsIncluded
            && row.EligibilityDate == new DateOnly(2026, 10, 15));
        Assert.Contains(page.Items, row => row.EmployeeId == 6 && !row.IsIncluded
            && row.ExclusionReasonCode == "NoApprovedSalary");
        Assert.DoesNotContain(page.Items, row => row.EmployeeId == 5);
    }

    [Fact]
    public async Task FinalSettlementUsesApprovedPayrollEvidenceAndCreatesImmutableIdempotentRevisions()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var payrollRun = await CreateAsync(new("Business", null, "final-settlement-run", 2));
        await CalculateAsync(payrollRun.Id);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_periods SET status = {PayrollPeriodStatuses.Approved}, version = 3 WHERE id = {payrollRun.PayrollPeriodId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Approved}, version = version + 1, approval_evidence_hash = {new string('A', 64)}, decided_by_user_id = {101L}, decided_at_utc = {DateTime.UtcNow} WHERE id = {payrollRun.Id}");

        var template = new HrClearanceTemplate
        {
            BusinessId = 1,
            Code = "FINAL-SETTLEMENT",
            Name = "Final settlement test",
            ScopeType = HrClearanceTemplateScopeType.Business,
            VersionNo = 1,
            EffectiveFrom = new(2020, 1, 1),
            IsActive = true,
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.HrClearanceTemplates.Add(template);
        await db.SaveChangesAsync();
        var exitCase = new HrExitCase
        {
            BusinessId = 1,
            EmployeeId = 1,
            ExitType = "Resignation",
            InitiatedDate = new(2026, 10, 1),
            ProposedLastWorkingDate = new(2026, 10, 31),
            ActualLastWorkingDate = new(2026, 10, 31),
            Reason = "Test exit",
            Status = "Approved",
            InitiatedByUserId = 101,
            InitiatedAtUtc = DateTime.UtcNow,
            ClearanceTemplateId = template.Id,
            ClearanceTemplateVersionNo = 1,
            ClearanceTemplateSnapshotJson = "{}",
            Version = 3
        };
        db.HrExitCases.Add(exitCase);
        await db.SaveChangesAsync();

        var store = new PayrollFinalSettlementStore(db, TimeProvider.System);
        Task<PayrollFinalSettlementModel> Calculate(string key) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.final_settlement.calculated", "Payroll",
                    "Calculated a versioned final settlement from persisted Payroll evidence.",
                    "HrExitCase", exitCase.Id.ToString(CultureInfo.InvariantCulture)),
                token => store.CalculateAsync(1, 101, exitCase.Id, exitCase.Version, key, token),
                result => !result.IsReplay,
                default);

        var first = await Calculate("settlement-revision-one");
        Assert.Equal(1, first.RevisionNo);
        Assert.Equal(1000m, first.GrossDue);
        Assert.Equal(0m, first.TotalDeductions);
        Assert.Equal(1000m, first.NetSettlement);
        Assert.Contains(first.Lines, line => line.ComponentCode == "PAYROLL_NET_DUE"
            && line.Amount == 1000m && line.SourceType == "PayrollEmployeeResult");
        Assert.Contains(first.Lines, line => !line.IsApplicable
            && line.ComponentCode == "LEAVE_ENCASHMENT"
            && line.NotApplicableReason!.Contains("not converted to money", StringComparison.Ordinal));

        var replay = await Calculate("settlement-revision-one");
        Assert.True(replay.IsReplay);
        Assert.Equal(first.SnapshotHash, replay.SnapshotHash);
        Assert.Single(await db.PayrollFinalSettlementRevisions.ToArrayAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => audit.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.final_settlement.calculated", "Payroll",
                "Calculated a versioned final settlement from persisted Payroll evidence.",
                "HrExitCase", exitCase.Id.ToString(CultureInfo.InvariantCulture)),
            token => store.CalculateAsync(1, 101, exitCase.Id, exitCase.Version + 1,
                "settlement-revision-one", token),
            _ => true,
            default));

        var second = await Calculate("settlement-revision-two");
        Assert.Equal(2, second.RevisionNo);
        Assert.Equal(first.SnapshotHash, second.SnapshotHash);
        Assert.Equal(2, await db.PayrollFinalSettlementRevisions.CountAsync());

        var frozenRevision = await db.PayrollFinalSettlementRevisions
            .SingleAsync(row => row.RevisionNo == first.RevisionNo);
        frozenRevision.SnapshotJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(frozenRevision).State = EntityState.Unchanged;
    }

    [Fact]
    public async Task IdempotentReplayDoesNotReselectOrMutateFrozenEligibility()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var request = new CreatePayrollRunRequest("Business", null, "period-run-replay", 2);
        var first = await CreateAsync(request);
        var original = await db.PayrollRunEmployeeSelections.SingleAsync(row =>
            row.PayrollRunId == first.Id && row.EmployeeId == 1);

        var replay = await CreateAsync(request);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Id, replay.Id);
        var frozen = await db.PayrollRunEmployeeSelections.AsNoTracking().SingleAsync(row =>
            row.PayrollRunId == first.Id && row.EmployeeId == 1);
        Assert.Equal(original.EmployeeNameSnapshot, frozen.EmployeeNameSnapshot);
        await Assert.ThrowsAsync<PayrollRunConflictException>(() =>
            CreateAsync(request with { ScopeType = "Location", ScopeId = 10 }));
        await Assert.ThrowsAsync<PayrollRunConflictException>(() =>
            CreateAsync(request with { IdempotencyKey = "another-key" }));
    }

    [Fact]
    public async Task LocationRunUsesOnlyEffectivePrimaryLocationNotAdditionalAssignments()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        await db.OperationalLocationProjections.AddAsync(new OperationalLocationProjection
        {
            Id = 11, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
            SourceEntityId = "11", Name = "Second outlet", SourceVersion = 1
        });
        await AddEmployeeAsync(2, "ADDITIONAL", new(2026, 1, 1), new(2026, 1, 1), null, 11);
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1,
            EmployeeId = 2,
            OperationalLocationProjectionId = 10,
            AssignmentType = "Additional",
            EffectiveFrom = new(2026, 1, 1),
            LocationNameSnapshot = "Outlet",
            SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet",
            SourceEntityIdSnapshot = "10",
            LocationSourceVersionSnapshot = 1,
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var run = await CreateAsync(new("Location", 10, "location-run-1", 2));
        var items = await store.GetEligibilityPageAsync(1, 1, run.Id, 1, 100, default);
        Assert.Single(items.Items);
        Assert.Equal(1, Assert.Single(items.Items).EmployeeId);
    }

    [Fact]
    public async Task DuplicateEmployeeSelectionAndEvidenceEditsAreRejected()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var run = await CreateAsync(new("Business", null, "immutable-run", 2));
        var selection = await db.PayrollRunEmployeeSelections.SingleAsync(row => row.PayrollRunId == run.Id);
        selection.EmployeeNameSnapshot = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(selection).ReloadAsync();

        db.PayrollRunEmployeeSelections.Add(new PayrollRunEmployeeSelection
        {
            BusinessId = 1,
            PayrollRunId = run.Id,
            EmployeeId = selection.EmployeeId,
            EmployeeCodeSnapshot = selection.EmployeeCodeSnapshot,
            EmployeeNameSnapshot = selection.EmployeeNameSnapshot,
            LocationIdSnapshot = selection.LocationIdSnapshot,
            EmploymentPeriodId = selection.EmploymentPeriodId,
            EmploymentStartDateSnapshot = selection.EmploymentStartDateSnapshot,
            EmploymentEndDateSnapshot = selection.EmploymentEndDateSnapshot,
            EligibilityDate = selection.EligibilityDate,
            EmploymentStatusSnapshot = selection.EmploymentStatusSnapshot,
            SalaryVersionId = selection.SalaryVersionId,
            SalaryEffectiveFromSnapshot = selection.SalaryEffectiveFromSnapshot,
            IsIncluded = selection.IsIncluded,
            ExclusionReasonCode = selection.ExclusionReasonCode,
            SelectedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CalculationPersistsImmutableSalaryLinesAndSameRunReplay()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var run = await CreateAsync(new("Business", null, "calculate-run", 2));
        var calculated = await CalculateAsync(run.Id);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(0, calculated.ExceptionEmployeeCount);
        Assert.False(calculated.IsReplay);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);
        Assert.Equal("Calculated", details.Result.Status);
        Assert.Equal(1000m, details.Result.GrossEarnings);
        Assert.Equal(0m, details.Result.TotalDeductions);
        Assert.Equal(1000m, details.Result.NetPay);
        Assert.Equal("BASE_1", Assert.Single(details.Lines).ComponentCode);

        var replay = await CalculateAsync(run.Id);
        Assert.True(replay.IsReplay);
        Assert.Equal(calculated.CalculatedEmployeeCount, replay.CalculatedEmployeeCount);
        Assert.Single(await db.PayrollEmployeeResults.ToArrayAsync());
        Assert.Single(await db.PayrollEmployeeResultLines.ToArrayAsync());
        Assert.Single(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.run.calculated").ToArrayAsync());

        var result = await db.PayrollEmployeeResults.SingleAsync();
        result.NetPay = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(result).ReloadAsync();
    }

    [Fact]
    public async Task ApprovalLifecycleFreezesCalculatedEvidenceAndActivatesOnlyOnApproval()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var run = await CreateAsync(new("Business", null, "approval-run", 2));
        db.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
        {
            BusinessId = 1, EmployeeId = 1, VersionNo = 1, Version = 1,
            OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Cash,
            IsPrimary = true, IsActive = true, EffectiveFrom = new(2026, 1, 1),
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await CalculateAsync(run.Id);

        var snapshot = await store.ReadAsync(
            1, PayrollRunApprovalConstants.TargetType, run.Id.ToString(), default);
        Assert.NotNull(snapshot);
        var pending = new ApprovalInstance
        {
            Id = 88,
            BusinessId = 1,
            ProcessType = ApprovalProcessType.PayrollRun,
            TargetType = PayrollRunApprovalConstants.TargetType,
            TargetId = run.Id.ToString(),
            TargetVersion = snapshot.Version,
            RequestHash = snapshot.Hash,
            Status = ApprovalInstanceStatus.Pending
        };
        await audit.ExecuteAsync(
            new RequiredAuditOperation(1, "hr.approval.submitted", "HR",
                "Submitted Payroll Run approval.", "ApprovalInstance", "88"),
            async token =>
            {
                await store.PrepareSubmissionAsync(1, 101,
                    new SubmitApprovalCommand(ApprovalProcessType.PayrollRun,
                        PayrollRunApprovalConstants.TargetType, run.Id.ToString(),
                        ApprovalScopeType.Business, null, snapshot.Version, snapshot.Hash), token);
                await store.SubmissionCreatedAsync(1, pending, token);
                return pending;
            },
            _ => true,
            default);

        var periodStore = new PayrollPeriodStore(db);
        await audit.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.period.locked", "Payroll",
                "Locked a Payroll period.", "PayrollPeriod", "1"),
            token => periodStore.LockAsync(1, 101, 1, 2, token),
            default);

        var beforeDecision = await db.PayrollRuns.AsNoTracking().SingleAsync(row => row.Id == run.Id);
        Assert.Equal(PayrollRunStatuses.PendingApproval, beforeDecision.Status);
        Assert.Equal(snapshot.Hash, beforeDecision.ApprovalEvidenceHash);
        Assert.Null(beforeDecision.DecidedAtUtc);

        pending.Status = ApprovalInstanceStatus.Approved;
        await audit.ExecuteAsync(
            new RequiredAuditOperation(1, "hr.approval.decided", "HR",
                "Approved Payroll Run.", "ApprovalInstance", "88"),
            async token =>
            {
                await store.DecisionCompletedAsync(1, pending, 202, "Approved", token);
                return true;
            },
            _ => true,
            default);

        var approved = await db.PayrollRuns.AsNoTracking().SingleAsync(row => row.Id == run.Id);
        Assert.Equal(PayrollRunStatuses.Approved, approved.Status);
        Assert.Equal(88, approved.ApprovalInstanceId);
        Assert.Equal(202, approved.DecidedByUserId);
        Assert.Equal(snapshot.Hash, approved.ApprovalEvidenceHash);
        Assert.Single(await db.PayrollEmployeeResults.ToArrayAsync());

        var tracked = await db.PayrollRuns.SingleAsync(row => row.Id == run.Id);
        tracked.ApprovalEvidenceHash = new string('A', 64);
        tracked.Version++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(tracked).ReloadAsync();
    }

    [Fact]
    public async Task CalculationFreezesCurrencyRoundsLinesAndPersistsCanonicalSnapshotHash()
    {
        await AddSalaryAsync(1, new(2026, 1, 1), baseAmount: 1000.125m);
        var now = DateTime.UtcNow;
        db.HrOrgUnits.Add(new HrOrgUnit
        {
            Id = 1, BusinessId = 1, Code = "STORE", Name = "Store", UnitType = "Outlet",
            OperationalLocationProjectionId = 10, EffectiveFrom = new(2020, 1, 1),
            CreatedAtUtc = now, CreatedByUserId = 101, UpdatedAtUtc = now, UpdatedByUserId = 101
        });
        await db.SaveChangesAsync();
        db.HrPositions.Add(new HrPosition
        {
            Id = 1, BusinessId = 1, Code = "CASHIER", Name = "Cashier", OrgUnitId = 1,
            OperationalLocationProjectionId = 10, Status = HrPositionStatus.Filled,
            EffectiveFrom = new(2020, 1, 1)
        });
        db.HrEmployeePositionAssignments.Add(new HrEmployeePositionAssignment
        {
            Id = 1, BusinessId = 1, EmployeeId = 1, PositionId = 1, OrgUnitId = 1,
            AssignmentType = HrEmployeeAssignmentType.Primary, EffectiveFrom = new(2020, 1, 1),
            IsCurrent = true
        });
        await db.SaveChangesAsync();
        var run = await CreateAsync(new("Business", null, "snapshot-rounding", 2));

        var persistedRun = await db.PayrollRuns.SingleAsync(row => row.Id == run.Id);
        Assert.Equal("BDT", persistedRun.CurrencyCode);
        Assert.Equal(2, persistedRun.DecimalPrecision);
        Assert.Equal("AwayFromZero", persistedRun.RoundingMode);
        Assert.Equal(await db.PayrollMonetaryConfigurationVersions
            .Where(row => row.BusinessId == 1).Select(row => row.Id).SingleAsync(),
            persistedRun.MonetaryConfigurationVersionId);

        await CalculateAsync(run.Id);

        var result = await db.PayrollEmployeeResults.SingleAsync();
        var line = await db.PayrollEmployeeResultLines.SingleAsync();
        Assert.Equal(1000.13m, line.Amount);
        Assert.Equal(1000.13m, result.GrossEarnings);
        Assert.Equal(1000.13m, result.NetPay);
        Assert.NotNull(result.CalculationSnapshotJson);
        Assert.Equal(64, result.CalculationSnapshotHash?.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(result.CalculationSnapshotJson!))), result.CalculationSnapshotHash);
        using var snapshot = JsonDocument.Parse(result.CalculationSnapshotJson!);
        Assert.Equal("BDT", snapshot.RootElement.GetProperty("currency").GetProperty("currencyCode").GetString());
        Assert.Equal(persistedRun.MonetaryConfigurationVersionId,
            snapshot.RootElement.GetProperty("currency")
                .GetProperty("monetaryConfigurationVersionId").GetInt64());
        Assert.Equal("ComponentOutput", snapshot.RootElement.GetProperty("payrollRoundingStage").GetString());
        Assert.Equal(1000.13m, snapshot.RootElement.GetProperty("lines")[0].GetProperty("amount").GetDecimal());
        var employeeSnapshot = snapshot.RootElement.GetProperty("employee");
        Assert.Equal("CASHIER", employeeSnapshot.GetProperty("positionCode").GetString());
        Assert.Equal("Cashier", employeeSnapshot.GetProperty("positionName").GetString());
        Assert.Equal("Outlet", employeeSnapshot.GetProperty("locationName").GetString());
        Assert.Equal("10", employeeSnapshot.GetProperty("locationReference").GetString());
        Assert.Equal("Outlet", employeeSnapshot.GetProperty("locationType").GetString());

        var position = await db.HrPositions.SingleAsync(row => row.Id == 1);
        var location = await db.OperationalLocationProjections.SingleAsync(row => row.Id == 10);
        position.Name = "Current Cashier";
        location.Name = "Current Outlet";
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Approved}, approval_instance_id = {88L}, decided_by_user_id = {202L}, decided_at_utc = {DateTime.UtcNow} WHERE id = {run.Id}");
        QuestPDF.Settings.License = LicenseType.Community;
        var source = await new PayrollPayslipStore(db).GetSourceAsync(1, run.Id, 1, default);
        var pdf = new PayrollPayslipPdfRenderer().Render(source, "payslip-v1");
        using (var rendered = PdfDocument.Open(pdf))
        {
            var renderedText = string.Join('\n', rendered.GetPages().Select(page => page.Text));
            Assert.Contains("Cashier (CASHIER)", renderedText);
            Assert.Contains("Historical location: Outlet", renderedText);
            Assert.DoesNotContain("Current Cashier", renderedText);
            Assert.DoesNotContain("Current Outlet", renderedText);
            Assert.Contains($"Gross earnings: BDT {source.GrossEarnings.ToString("N2", CultureInfo.InvariantCulture)}",
                renderedText);
            Assert.Contains($"Total deductions: BDT {source.TotalDeductions.ToString("N2", CultureInfo.InvariantCulture)}",
                renderedText);
            Assert.Contains($"Net payable: BDT {source.NetPayable.ToString("N2", CultureInfo.InvariantCulture)}",
                renderedText);
            Assert.Contains($"BDT {source.Lines.Single().Amount.ToString("N2", CultureInfo.InvariantCulture)}",
                renderedText);
        }

        var originalHash = result.CalculationSnapshotHash;
        result.CalculationSnapshotJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(result).ReloadAsync();
        Assert.Equal(originalHash, result.CalculationSnapshotHash);
    }

    [Fact]
    public async Task CalculationUsesFrozenCurrencyPrecisionForRounding()
    {
        db.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
        {
            BusinessId = 1,
            VersionNo = 2,
            EffectiveFrom = new(2026, 10, 1),
            CurrencyCode = "JPY",
            DecimalPrecision = 0,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101
        });
        await db.SaveChangesAsync();
        await AddSalaryAsync(1, new(2026, 1, 1), baseAmount: 1000.5m);
        var run = await CreateAsync(new("Business", null, "zero-precision-rounding", 2));

        await CalculateAsync(run.Id);

        var result = await db.PayrollEmployeeResults.SingleAsync();
        var line = await db.PayrollEmployeeResultLines.SingleAsync();
        Assert.Equal(1001m, line.Amount);
        Assert.Equal(1001m, result.GrossEarnings);
        Assert.Equal(1001m, result.NetPay);
        Assert.Equal(0, (await db.PayrollRuns.SingleAsync()).DecimalPrecision);
    }

    [Fact]
    public async Task ConfidentialResultReadRejectsTamperedCalculationSnapshot()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var run = await CreateAsync(new("Business", null, "tampered-snapshot", 2));
        await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_employee_results SET calculation_snapshot_json = '{{}}' WHERE id = {result.Id}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            calculations.GetAsync(1, 1, run.Id, 1, default));
    }

    [Fact]
    public async Task PercentageComponentWithoutConfiguredBaseProducesExplicitException()
    {
        await AddSalaryAsync(1, new(2026, 1, 1), "Percentage");
        var run = await CreateAsync(new("Business", null, "calculate-percentage", 2));

        var calculated = await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync();

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal(1, calculated.ExceptionEmployeeCount);
        Assert.Equal("Exception", result.Status);
        Assert.Equal("PercentageBasisNotConfigured", result.ExceptionCode);
        Assert.Null(result.GrossEarnings);
        Assert.Empty(await db.PayrollEmployeeResultLines.ToArrayAsync());
    }

    [Fact]
    public async Task FormulaComponentsAreEvaluatedFromFrozenSalaryValues()
    {
        await AddSalaryAsync(1, new(2026, 1, 1), includeFormula: true);
        var run = await CreateAsync(new("Business", null, "calculate-formula", 2));

        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(1100m, details.Result.GrossEarnings);
        Assert.Collection(details.Lines,
            line => Assert.Equal(("BASE_1", 1000m), (line.ComponentCode, line.Amount)),
            line => Assert.Equal(("BONUS_1", 100m), (line.ComponentCode, line.Amount)));
    }

    [Fact]
    public async Task CalculationIncludesApprovedAdjustmentSnapshotsAndPreventsOneTimeReuse()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        await AddEmployeeAsync(2, "EMP-2", new(2026, 1, 1), new(2026, 1, 1), null, 10);
        await AddSalaryAsync(2, new(2026, 1, 1));
        var component = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var secondComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_2");
        var recurringIdentity = Guid.NewGuid();
        var oneTimeIdentity = Guid.NewGuid();
        db.PayrollEmployeeAdjustmentVersions.AddRange(
            new PayrollEmployeeAdjustmentVersion
            {
                BusinessId = 1,
                EmployeeId = 1,
                AdjustmentIdentity = recurringIdentity,
                Version = 1,
                SalaryComponentId = component.Id,
                ComponentCodeSnapshot = component.Code,
                ComponentNameSnapshot = component.Name,
                ComponentTypeSnapshot = "Earning",
                ValueTypeSnapshot = "Fixed",
                AdjustmentKind = "RecurringAllowance",
                Classification = "Manual",
                Amount = 100m,
                EffectiveFrom = new(2026, 1, 1),
                OperationalLocationProjectionId = 10,
                Reason = "Recurring adjustment",
                Status = "Active",
                IdempotencyKey = "recurring",
                IdempotencyPayloadHash = new('D', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            },
            new PayrollEmployeeAdjustmentVersion
            {
                BusinessId = 1,
                EmployeeId = 1,
                AdjustmentIdentity = oneTimeIdentity,
                Version = 1,
                SalaryComponentId = component.Id,
                ComponentCodeSnapshot = component.Code,
                ComponentNameSnapshot = component.Name,
                ComponentTypeSnapshot = "Earning",
                ValueTypeSnapshot = "Fixed",
                AdjustmentKind = "OneTimeEarning",
                Classification = "Incentive",
                Amount = 50m,
                EffectiveFrom = new(2026, 10, 15),
                OperationalLocationProjectionId = 10,
                Reason = "One-time adjustment",
                Status = "Active",
                IdempotencyKey = "one-time",
                IdempotencyPayloadHash = new('E', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            },
            new PayrollEmployeeAdjustmentVersion
            {
                BusinessId = 1,
                EmployeeId = 2,
                AdjustmentIdentity = recurringIdentity,
                Version = 1,
                SalaryComponentId = secondComponent.Id,
                ComponentCodeSnapshot = secondComponent.Code,
                ComponentNameSnapshot = secondComponent.Name,
                ComponentTypeSnapshot = "Earning",
                ValueTypeSnapshot = "Fixed",
                AdjustmentKind = "RecurringAllowance",
                Classification = "Manual",
                Amount = 200m,
                EffectiveFrom = new(2026, 1, 1),
                OperationalLocationProjectionId = 10,
                Reason = "Employee-specific recurring adjustment",
                Status = "Active",
                IdempotencyKey = "recurring-employee-2",
                IdempotencyPayloadHash = new('1', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var firstRun = await CreateAsync(new("Business", null, "calculate-adjustments-1", 2));
        var calculated = await CalculateAsync(firstRun.Id);
        var details = await calculations.GetAsync(1, 1, firstRun.Id, 1, default);

        Assert.Equal(2, calculated.CalculatedEmployeeCount);
        Assert.Equal(1150m, details.Result.GrossEarnings);
        Assert.Contains(details.Lines, line =>
            line.SourceType == "RecurringAllowance" && line.SourceVersion == 1 && line.Amount == 100m);
        Assert.Contains(details.Lines, line =>
            line.SourceType == "ManualEarning" && line.SourceId == $"1:{oneTimeIdentity:N}:1"
                && line.Amount == 50m);
        var secondEmployee = await calculations.GetAsync(1, 1, firstRun.Id, 2, default);
        Assert.Equal(1200m, secondEmployee.Result.GrossEarnings);
        Assert.Contains(secondEmployee.Lines, line =>
            line.SourceType == "RecurringAllowance" && line.SourceVersion == 1 && line.Amount == 200m);

        db.PayrollPeriods.Add(new PayrollPeriod
        {
            BusinessId = 1,
            PeriodCode = "2026-10-overlap",
            PeriodStart = new(2026, 10, 15),
            PeriodEnd = new(2026, 11, 14),
            Status = PayrollPeriodStatuses.Processing,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow,
            OpenedByUserId = 101,
            ProcessingAtUtc = DateTime.UtcNow,
            ProcessingByUserId = 101
        });
        await db.SaveChangesAsync();
        var overlapPeriod = await db.PayrollPeriods.SingleAsync(row =>
            row.PeriodCode == "2026-10-overlap");
        var secondRun = await CreateAsync(overlapPeriod.Id,
            new("Business", null, "calculate-adjustments-2", 1));
        var secondCalculation = await CalculateAsync(overlapPeriod.Id, secondRun.Id);
        var secondResult = await db.PayrollEmployeeResults.SingleAsync(row =>
            row.PayrollRunId == secondRun.Id && row.EmployeeId == 1);
        Assert.Equal(1, secondCalculation.CalculatedEmployeeCount);
        Assert.Equal("OneTimeAdjustmentAlreadyConsumed", secondResult.ExceptionCode);
        Assert.Empty(await db.PayrollEmployeeResultLines.Where(row =>
            row.PayrollEmployeeResultId == secondResult.Id).ToArrayAsync());
    }

    [Fact]
    public async Task CalculationCapsDueLoanRecoveryAtOutstandingBalanceWithoutMutatingLoan()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var loan = new PayrollEmployeeLoan
        {
            BusinessId = 1,
            EmployeeId = 1,
            OperationalLocationProjectionId = 10,
            LoanType = "Advance",
            PrincipalAmount = 500m,
            ApprovedAmount = 500m,
            DisbursedDate = new(2026, 9, 1),
            RecoveryStartDate = new(2026, 10, 1),
            RecoveryMode = "CustomSchedule",
            OutstandingAmount = 300m,
            ProposedScheduleJson = "[]",
            RequestHash = new('F', 64),
            IdempotencyKey = "loan",
            IdempotencyPayloadHash = new('A', 64),
            Reason = "Test loan",
            Status = "Active",
            CurrentScheduleVersionNo = 1,
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.PayrollEmployeeLoans.Add(loan);
        await db.SaveChangesAsync();
        var schedule = new PayrollLoanScheduleVersion
        {
            BusinessId = 1,
            LoanId = loan.Id,
            VersionNo = 1,
            RecoveryStartDate = new(2026, 10, 1),
            RecoveryMode = "CustomSchedule",
            IdempotencyKey = "loan-schedule",
            IdempotencyPayloadHash = new('B', 64),
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.PayrollLoanScheduleVersions.Add(schedule);
        await db.SaveChangesAsync();
        db.PayrollLoanScheduleLines.Add(new PayrollLoanScheduleLine
        {
            BusinessId = 1,
            LoanId = loan.Id,
            ScheduleVersionId = schedule.Id,
            InstallmentNo = 1,
            DueDate = new(2026, 10, 15),
            PlannedAmount = 400m
        });
        await db.SaveChangesAsync();

        var run = await CreateAsync(new("Business", null, "calculate-loan", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(1000m, details.Result.GrossEarnings);
        Assert.Equal(300m, details.Result.TotalDeductions);
        Assert.Equal(700m, details.Result.NetPay);
        var recovery = Assert.Single(details.Lines, line => line.SourceType == "LoanRecovery");
        Assert.Equal(300m, recovery.Amount);
        Assert.Equal(400m, recovery.InputAmount);
        Assert.Equal(300m, (await db.PayrollEmployeeLoans.SingleAsync(row => row.Id == loan.Id))
            .OutstandingAmount);
    }

    [Fact]
    public async Task AttendanceWithoutConfiguredPayrollRuleCreatesExplicitException()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        await AddAttendanceAsync(new(2026, 10, 15), "Absent");

        var run = await CreateAsync(new("Business", null, "calculate-attendance", 2));
        var calculated = await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal(1, calculated.ExceptionEmployeeCount);
        Assert.Equal("AttendanceDeductionPolicyNotConfigured", result.ExceptionCode);
        Assert.Empty(await db.PayrollEmployeeResultLines.Where(row =>
            row.PayrollEmployeeResultId == result.Id).ToArrayAsync());
    }

    [Fact]
    public async Task EffectivePolicyPricesAbsenceAndLateFromFrozenSalaryAndMappingSnapshots()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("ABSENCE", "Deduction");
        var lateComponent = AddPolicyComponent("LATE", "Deduction");
        await db.SaveChangesAsync();
        var policy = await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30", [baseComponent.Id],
            true, false, 2m, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, lateComponent.Id,
            null, null, null));
        absenceComponent.Name = "Renamed after policy publication";
        await db.SaveChangesAsync();

        await AddAttendanceAsync(new(2026, 10, 15), "Absent");
        var lateAttendance = await AddAttendanceAsync(new(2026, 10, 16), "Present");
        lateAttendance.LateMinutes = 3;
        await db.SaveChangesAsync();
        var run = await CreateAsync(new("Business", null, "calculate-configured-rules", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(39.33m, details.Result.TotalDeductions);
        Assert.Equal(960.67m, details.Result.NetPay);
        var persisted = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);
        Assert.Equal(policy.Id, persisted.CalculationPolicyVersionId);
        Assert.Contains("\"code\":\"ABSENCE\"", persisted.CalculationPolicySnapshotJson);
        var absence = Assert.Single(details.Lines, line => line.SourceType == "AttendanceAbsence");
        Assert.Equal("ABSENCE", absence.ComponentCode);
        Assert.Equal("Absence", absence.ComponentName);
        Assert.Equal(33.333333333333m, decimal.Round(absence.InputRate!.Value, 12));
        Assert.Equal(3m, Assert.Single(details.Lines, line => line.SourceType == "AttendanceLate").InputQuantity);
    }

    [Fact]
    public async Task WorkingDaysBasisUsesPublishedRosterAndIncludesAbsentWorkdays()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("WORKDAY_ABSENCE", "Deduction");
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "WorkingDays", [baseComponent.Id],
            true, false, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, null, null, null, null));

        await AddAttendanceAsync(new(2026, 10, 15), "Absent");
        await AddAttendanceAsync(new(2026, 10, 16), "Present");
        var run = await CreateAsync(new("Business", null, "calculate-working-days", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(500m, details.Result.TotalDeductions);
        Assert.Equal(500m, Assert.Single(details.Lines,
            line => line.SourceType == "AttendanceAbsence").InputRate);
    }

    [Fact]
    public async Task WorkingDaysBasisBlocksWhenPublishedRosterAttendanceIsMissing()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("MISSING_ATTENDANCE_ABSENCE", "Deduction");
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "WorkingDays", [baseComponent.Id],
            true, false, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, null, null, null, null));

        await AddAttendanceAsync(new(2026, 10, 15), "Absent");
        await AddRosterOnlyAsync(new(2026, 10, 16));
        var run = await CreateAsync(new("Business", null, "calculate-missing-attendance", 2));
        var calculated = await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal("IncompleteAttendanceEvidence", result.ExceptionCode);
        Assert.Empty(await db.PayrollEmployeeResultLines.Where(row =>
            row.PayrollEmployeeResultId == result.Id).ToArrayAsync());
    }

    [Fact]
    public async Task CalendarDaysBasisPricesEachAbsenceByItsOwnCalendarMonth()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("CALENDAR_ABSENCE", "Deduction");
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 11, 30), "CalendarDays", [baseComponent.Id],
            true, false, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, null, null, null, null));
        await AddAttendanceAsync(new(2026, 10, 31), "Absent");
        await AddAttendanceAsync(new(2026, 11, 1), "Absent");

        db.PayrollPeriods.Add(new PayrollPeriod
        {
            BusinessId = 1,
            PeriodCode = "2026-10-cross-month",
            PeriodStart = new(2026, 10, 15),
            PeriodEnd = new(2026, 11, 14),
            Status = PayrollPeriodStatuses.Processing,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow,
            OpenedByUserId = 101,
            ProcessingAtUtc = DateTime.UtcNow,
            ProcessingByUserId = 101
        });
        await db.SaveChangesAsync();
        var period = await db.PayrollPeriods.SingleAsync(row => row.PeriodCode == "2026-10-cross-month");
        var run = await CreateAsync(period.Id, new("Business", null, "calculate-calendar-days", 1));
        var calculated = await CalculateAsync(period.Id, run.Id);
        var details = await calculations.GetAsync(1, period.Id, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(65.59m, details.Result.TotalDeductions);
        Assert.Equal([32.258064516129m, 33.333333333333m], details.Lines
            .Where(line => line.SourceType == "AttendanceAbsence")
            .Select(line => decimal.Round(line.InputRate!.Value, 12)).Order().ToArray());
    }

    [Fact]
    public async Task PolicyChangesInsidePayrollPeriodCreateExplicitException()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("MID_PERIOD_ABSENCE", "Deduction");
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30", [baseComponent.Id],
            true, false, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, null, null, null, null));
        await CreateCalculationPolicyAsync(new(
            new(2026, 11, 1), new(2026, 11, 30), "Fixed30", [baseComponent.Id],
            true, false, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, null, null, null, null, null));
        await AddAttendanceAsync(new(2026, 10, 31), "Absent");
        await AddAttendanceAsync(new(2026, 11, 1), "Absent");

        db.PayrollPeriods.Add(new PayrollPeriod
        {
            BusinessId = 1,
            PeriodCode = "2026-10-mid-policy",
            PeriodStart = new(2026, 10, 15),
            PeriodEnd = new(2026, 11, 14),
            Status = PayrollPeriodStatuses.Processing,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow,
            OpenedByUserId = 101,
            ProcessingAtUtc = DateTime.UtcNow,
            ProcessingByUserId = 101
        });
        await db.SaveChangesAsync();
        var period = await db.PayrollPeriods.SingleAsync(row => row.PeriodCode == "2026-10-mid-policy");
        var run = await CreateAsync(period.Id, new("Business", null, "calculate-mid-policy", 1));

        var calculated = await CalculateAsync(period.Id, run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal("PayrollCalculationPolicyNotEffectiveForWholePeriod", result.ExceptionCode);
        Assert.Empty(await db.PayrollEmployeeResultLines.Where(row =>
            row.PayrollEmployeeResultId == result.Id).ToArrayAsync());
    }

    [Fact]
    public async Task ApprovedUnpaidLeaveTakesPrecedenceOverAbsenceAndUsesConfiguredDailyRate()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var absenceComponent = AddPolicyComponent("ABSENCE", "Deduction");
        var unpaidComponent = AddPolicyComponent("UNPAID", "Deduction");
        await db.SaveChangesAsync();
        var policy = new HrLeavePolicyVersion
        {
            BusinessId = 1, LeaveTypeId = 0, Code = "UNPAID", Name = "Unpaid",
            ScopeType = HrLeavePolicyScopeType.Business, VersionNo = 1,
            AccrualMethod = HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart,
            AnnualEntitlement = 0m, LeaveYearStartMonth = 1, LeaveYearStartDay = 1,
            EffectiveFrom = new(2026, 1, 1), IsActive = true
        };
        var leaveType = new HrLeaveType
        {
            BusinessId = 1, Code = "UNPAID", Name = "Unpaid leave", IsPaid = false, IsActive = true
        };
        db.HrLeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        policy.LeaveTypeId = leaveType.Id;
        db.HrLeavePolicyVersions.Add(policy);
        await db.SaveChangesAsync();
        var leaveDay = new HrLeaveRequestDay
        {
            BusinessId = 1, EmployeeId = 1, WorkDate = new(2026, 10, 15),
            OperationalLocationProjectionId = 10, PolicyVersionId = policy.Id,
            Units = 1m, DayClassification = "NormalWorkingDay",
            ShiftSnapshotJson = "{}", CalendarEvidenceJson = "{}"
        };
        db.HrLeaveRequests.Add(new HrLeaveRequest
        {
            BusinessId = 1, EmployeeId = 1, LeaveTypeId = leaveType.Id,
            ScopeLocationProjectionId = 10, StartDate = leaveDay.WorkDate, EndDate = leaveDay.WorkDate,
            RequestedUnits = 1m, Reason = "Approved unpaid leave", Status = "Approved",
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            DecidedByUserId = 202, DecidedAtUtc = DateTime.UtcNow,
            RequestHash = new('D', 64), Days = [leaveDay]
        });
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30", [baseComponent.Id],
            true, true, null, null, null, 30, 480,
            null, null, null, null, absenceComponent.Id, unpaidComponent.Id,
            null, null, null, null));
        await AddAttendanceAsync(new(2026, 10, 15), "Absent");

        var run = await CreateAsync(new("Business", null, "calculate-unpaid-precedence", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(33.33m, details.Result.TotalDeductions);
        Assert.Single(details.Lines, line => line.SourceType == "UnpaidLeave");
        Assert.DoesNotContain(details.Lines, line => line.SourceType == "AttendanceAbsence");
    }

    [Theory]
    [InlineData("FixedHourlyRate", 20, 10)]
    [InlineData("SalaryBaseMultiplier", 1.5, 3.13)]
    public async Task ApprovedOvertimeUsesConfiguredModeAndFrozenCategoryMapping(
        string mode, decimal categoryRate, decimal expectedAmount)
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var baseComponent = await db.PayrollSalaryComponents.SingleAsync(row => row.Code == "BASE_1");
        var overtimeComponent = AddPolicyComponent("OVERTIME", "Earning");
        await db.SaveChangesAsync();
        var attendance = await AddAttendanceAsync(new(2026, 10, 15), "Present");
        var overtimePolicy = new HrOvertimePolicyVersion
        {
            BusinessId = 1, Code = "OT-2026", Name = "Overtime",
            ScopeType = HrOvertimePolicyScopeType.Business, VersionNo = 1,
            EffectiveFrom = new(2026, 1, 1), MinimumEligibleMinutes = 1,
            RoundingUnitMinutes = 1, RoundingMethod = HrOvertimeRoundingMethod.Down,
            RateType = HrOvertimeRateType.Multiplier, NormalDayRate = 1m,
            WeeklyOffRate = 1m, HolidayRate = 1m, SpecialDayRate = 1m, IsActive = true
        };
        db.Set<HrOvertimePolicyVersion>().Add(overtimePolicy);
        var approvalPolicy = new ApprovalPolicy
        {
            BusinessId = 1, Code = "OT-APPROVAL", ProcessType = ApprovalProcessType.Overtime,
            Name = "Overtime approval", VersionNo = 1, ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2026, 1, 1), IsActive = true
        };
        db.ApprovalPolicies.Add(approvalPolicy);
        await db.SaveChangesAsync();
        var approval = new ApprovalInstance
        {
            BusinessId = 1, PolicyId = approvalPolicy.Id,
            PolicyCodeSnapshot = approvalPolicy.Code, PolicyVersionSnapshot = 1,
            ProcessType = ApprovalProcessType.Overtime, TargetType = "HrOvertimeEntry",
            TargetId = "1", RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business, TargetVersion = 1, RequestHash = new('A', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();
        db.HrOvertimeEntries.Add(new HrOvertimeEntry
        {
            BusinessId = 1, EmployeeId = 1, WorkDate = attendance.WorkDate,
            DailyAttendanceId = attendance.Id, OperationalLocationProjectionId = 10,
            DayCategory = "NormalWorkingDay", AttendanceProcessingVersion = attendance.ProcessingVersion,
            PolicyVersionId = overtimePolicy.Id, PolicyVersionNo = overtimePolicy.VersionNo,
            EligibleMinutes = 30, CalculatedMinutes = 30, ApprovedMinutes = 30,
            Status = "PayrollEligible", CalculationVersion = 1,
            CalculationFingerprint = new('E', 64), RequestHash = new('F', 64),
            PolicySnapshotJson = "{}", AttendanceSnapshotJson = "{}", CalculationInputsJson = "{}",
            ApprovalInstanceId = approval.Id
        });
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30",
            mode == "SalaryBaseMultiplier" ? [baseComponent.Id] : [],
            false, false, null, null, mode, 30, 480,
            categoryRate, categoryRate, categoryRate, categoryRate,
            null, null, null, null, overtimeComponent.Id, null));

        var run = await CreateAsync(new("Business", null, $"calculate-ot-{mode}", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        var line = Assert.Single(details.Lines, row => row.SourceType == "ApprovedOvertime");
        Assert.Equal(expectedAmount, line.Amount);
        Assert.Equal(30m, line.InputQuantity);
        Assert.Equal("OVERTIME", line.ComponentCode);
    }

    [Fact]
    public async Task CalculationPolicyVersionsRejectOverlapAndCannotBeRewritten()
    {
        var request = new CreatePayrollCalculationPolicyRequest(
            new(2026, 10, 1), new(2026, 10, 31), null, [],
            false, false, null, null, null, 30, 0,
            null, null, null, null, null, null, null, null, null, null);
        var created = await CreateCalculationPolicyAsync(request);
        Assert.Equal("Fixed30", created.PartialMonthBasis);
        await Assert.ThrowsAsync<PayrollCalculationPolicyConflictException>(() =>
            CreateCalculationPolicyAsync(request with
            {
                EffectiveFrom = new(2026, 10, 15),
                EffectiveTo = new(2026, 11, 15)
            }));
        var stored = await db.PayrollCalculationPolicyVersions.SingleAsync(row => row.Id == created.Id);
        stored.PartialMonthBasis = "WorkingDays";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConfiguredTaxStatutoryMappingConsumesManualDeductionWithoutCalculatingTax()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var taxComponent = AddPolicyComponent("STATUTORY", "Deduction");
        await db.SaveChangesAsync();
        db.PayrollEmployeeAdjustmentVersions.Add(new PayrollEmployeeAdjustmentVersion
        {
            BusinessId = 1, EmployeeId = 1, AdjustmentIdentity = Guid.NewGuid(), Version = 1,
            SalaryComponentId = taxComponent.Id, ComponentCodeSnapshot = taxComponent.Code,
            ComponentNameSnapshot = taxComponent.Name, ComponentTypeSnapshot = "Deduction",
            ValueTypeSnapshot = "Fixed", AdjustmentKind = "OneTimeDeduction",
            Classification = "Statutory", Amount = 75m, EffectiveFrom = new(2026, 10, 15),
            OperationalLocationProjectionId = 10, Reason = "Manual statutory deduction",
            Status = "Active", IdempotencyKey = "tax-manual", IdempotencyPayloadHash = new('D', 64),
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await CreateCalculationPolicyAsync(new(
            new(2026, 10, 1), new(2026, 10, 31), "Fixed30", [],
            false, false, null, null, null, 30, 0,
            null, null, null, null, null, null, null, null, null, taxComponent.Id));

        var run = await CreateAsync(new("Business", null, "calculate-manual-statutory", 2));
        var calculated = await CalculateAsync(run.Id);
        var details = await calculations.GetAsync(1, 1, run.Id, 1, default);

        Assert.Equal(1, calculated.CalculatedEmployeeCount);
        Assert.Equal(75m, details.Result.TotalDeductions);
        var line = Assert.Single(details.Lines, row => row.SourceType == "TaxStatutory");
        Assert.Equal(75m, line.Amount);
        Assert.Equal("STATUTORY", line.ComponentCode);
    }

    [Fact]
    public async Task ApprovedUnpaidLeaveWithoutConfiguredDeductionRuleCreatesExplicitException()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var leaveType = new HrLeaveType
        {
            BusinessId = 1,
            Code = "UNPAID",
            Name = "Unpaid leave",
            IsPaid = false,
            IsActive = true
        };
        db.HrLeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        var policy = new HrLeavePolicyVersion
        {
            BusinessId = 1,
            LeaveTypeId = leaveType.Id,
            Code = "UNPAID-2026",
            Name = "Unpaid leave",
            ScopeType = HrLeavePolicyScopeType.Business,
            VersionNo = 1,
            AccrualMethod = HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart,
            AnnualEntitlement = 0m,
            LeaveYearStartMonth = 1,
            LeaveYearStartDay = 1,
            EffectiveFrom = new(2026, 1, 1),
            IsActive = true
        };
        db.HrLeavePolicyVersions.Add(policy);
        await db.SaveChangesAsync();
        var request = new HrLeaveRequest
        {
            BusinessId = 1,
            EmployeeId = 1,
            LeaveTypeId = leaveType.Id,
            ScopeLocationProjectionId = 10,
            StartDate = new(2026, 10, 15),
            EndDate = new(2026, 10, 15),
            RequestedUnits = 1m,
            Reason = "Test unpaid leave",
            Status = "Approved",
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            DecidedByUserId = 202,
            DecidedAtUtc = DateTime.UtcNow,
            RequestHash = new('D', 64),
            Days =
            [
                new HrLeaveRequestDay
                {
                    BusinessId = 1,
                    EmployeeId = 1,
                    WorkDate = new(2026, 10, 15),
                    OperationalLocationProjectionId = 10,
                    PolicyVersionId = policy.Id,
                    Units = 1m,
                    DayClassification = "NormalWorkingDay",
                    ShiftSnapshotJson = "{}",
                    CalendarEvidenceJson = "{}"
                }
            ]
        };
        db.HrLeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var run = await CreateAsync(new("Business", null, "calculate-unpaid-leave", 2));
        var calculated = await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal(1, calculated.ExceptionEmployeeCount);
        Assert.Equal("UnpaidLeaveDeductionPolicyNotConfigured", result.ExceptionCode);
    }

    [Fact]
    public async Task PayrollEligibleOvertimeWithoutApprovedDecisionCannotBeIgnoredOrPriced()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var attendance = await AddAttendanceAsync(new(2026, 10, 15), "Present");
        var policy = new HrOvertimePolicyVersion
        {
            BusinessId = 1,
            Code = "OT-2026",
            Name = "Overtime",
            ScopeType = HrOvertimePolicyScopeType.Business,
            VersionNo = 1,
            EffectiveFrom = new(2026, 1, 1),
            MinimumEligibleMinutes = 1,
            RoundingUnitMinutes = 1,
            RoundingMethod = HrOvertimeRoundingMethod.Down,
            RateType = HrOvertimeRateType.Multiplier,
            NormalDayRate = 1m,
            WeeklyOffRate = 1m,
            HolidayRate = 1m,
            SpecialDayRate = 1m,
            IsActive = true
        };
        db.Set<HrOvertimePolicyVersion>().Add(policy);
        await db.SaveChangesAsync();
        db.Set<HrOvertimeEntry>().Add(new HrOvertimeEntry
        {
            BusinessId = 1,
            EmployeeId = 1,
            WorkDate = new(2026, 10, 15),
            DailyAttendanceId = attendance.Id,
            OperationalLocationProjectionId = 10,
            DayCategory = "NormalWorkingDay",
            AttendanceProcessingVersion = attendance.ProcessingVersion,
            PolicyVersionId = policy.Id,
            PolicyVersionNo = policy.VersionNo,
            EligibleMinutes = 30,
            CalculatedMinutes = 30,
            ApprovedMinutes = 30,
            Status = "PayrollEligible",
            CalculationVersion = 1,
            CalculationFingerprint = new('E', 64),
            RequestHash = new('F', 64),
            PolicySnapshotJson = "{}",
            AttendanceSnapshotJson = "{}",
            CalculationInputsJson = "{}"
        });
        await db.SaveChangesAsync();

        var run = await CreateAsync(new("Business", null, "calculate-overtime", 2));
        var calculated = await CalculateAsync(run.Id);
        var result = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);

        Assert.Equal(0, calculated.CalculatedEmployeeCount);
        Assert.Equal("IncompleteOvertimeApprovalEvidence", result.ExceptionCode);
    }

    [Fact]
    public async Task ChangedInputCreatesImmutableVersionedRecalculationAndReadsOnlyCurrentVersion()
    {
        await AddSalaryAsync(1, new(2026, 1, 1));
        var run = await CreateAsync(new("Business", null, "calculate-recalculation", 2));
        var first = await CalculateAsync(run.Id);
        var firstResult = await db.PayrollEmployeeResults.SingleAsync(row => row.PayrollRunId == run.Id);
        var firstSnapshotHash = firstResult.CalculationSnapshotHash;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_periods SET version = {3L} WHERE id = {1L}");
        var recalculated = await CalculateAsync(run.Id);
        var results = await db.PayrollEmployeeResults.Where(row => row.PayrollRunId == run.Id)
            .OrderBy(row => row.CalculationVersion).ToArrayAsync();
        var currentPage = await calculations.GetPageAsync(1, 1, run.Id, 1, 100, default);

        Assert.False(first.IsReplay);
        Assert.False(recalculated.IsReplay);
        Assert.Equal(2, results.Length);
        Assert.Equal(new[] { 1, 2 }, results.Select(row => row.CalculationVersion));
        Assert.Equal(firstResult.CalculationInputHash, results[0].CalculationInputHash);
        Assert.NotEqual(results[0].CalculationInputHash, results[1].CalculationInputHash);
        Assert.Equal(firstSnapshotHash, results[0].CalculationSnapshotHash);
        Assert.Equal(results[0].CalculationSnapshotHash, results[1].CalculationSnapshotHash);
        Assert.Equal(results[0].CalculationSnapshotJson, results[1].CalculationSnapshotJson);
        Assert.NotNull(results[0].CalculationSnapshotJson);
        Assert.NotNull(results[1].CalculationSnapshotJson);
        Assert.Equal(1000m, currentPage.Items.Single().GrossEarnings);
        Assert.Equal(1, currentPage.TotalCount);
        Assert.Equal(2, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.run.calculated"));
    }

    [Fact]
    public async Task RunCannotBeCreatedForWrongBusinessOrStaleOrNonProcessingPeriod()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            audit.ExecuteAsync(Operation(), token =>
                store.CreateAsync(2, 101, 1, new("Business", null, "foreign", 2), token), default));
        await Assert.ThrowsAsync<PayrollRunConflictException>(() =>
            CreateAsync(new("Business", null, "stale", 1)));
        var period = await db.PayrollPeriods.SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_periods SET status = {PayrollPeriodStatuses.Open}, version = 3 WHERE id = {period.Id}");
        await Assert.ThrowsAsync<PayrollRunConflictException>(() =>
            CreateAsync(new("Business", null, "not-processing", 3)));
        Assert.Empty(await db.PayrollRuns.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private Task<PayrollRunResponse> CreateAsync(CreatePayrollRunRequest request) =>
        CreateAsync(1, request);

    private Task<PayrollRunResponse> CreateAsync(long periodId, CreatePayrollRunRequest request) =>
        audit.ExecuteAsync(Operation(),
            token => store.CreateAsync(1, 101, periodId, request, token),
            result => !result.IsReplay,
            default);

    private Task<PayrollRunCalculationResponse> CalculateAsync(long runId) =>
        CalculateAsync(1, runId);

    private Task<PayrollRunCalculationResponse> CalculateAsync(long periodId, long runId) =>
        audit.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.run.calculated", "Payroll",
                "Calculated payroll inputs for a Payroll run.", "PayrollRun", runId.ToString()),
            token => calculations.CalculateAsync(1, periodId, runId, token),
            result => !result.IsReplay,
            default);

    private Task<PayrollCalculationPolicyResponse> CreateCalculationPolicyAsync(
        CreatePayrollCalculationPolicyRequest request) =>
        audit.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.calculation_policy.created", "Payroll",
                "Created an immutable Payroll calculation policy version.",
                "PayrollCalculationPolicyVersion", request.EffectiveFrom.ToString("yyyy-MM-dd")),
            token => new PayrollCalculationPolicyStore(db).CreateAsync(1, 101, request, token),
            default);

    private PayrollSalaryComponent AddPolicyComponent(string code, string type)
    {
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1,
            Code = code,
            Name = type == "Earning" ? "Overtime earning" : code == "ABSENCE" ? "Absence" : "Payroll deduction",
            ComponentType = type,
            ValueType = "Fixed",
            IsRecurring = false,
            IsActive = true
        };
        db.PayrollSalaryComponents.Add(component);
        return component;
    }

    private static RequiredAuditOperation Operation() =>
        new(1, "payroll.run.created", "Payroll",
            "Created a Payroll run and captured eligibility evidence.", "PayrollRun", "1");

    private async Task<HrDailyAttendance> AddAttendanceAsync(DateOnly workDate, string status)
    {
        var shift = new HrShift
        {
            BusinessId = 1,
            Code = $"PAYROLL-{workDate:yyyyMMdd}",
            Name = "Payroll test",
            StartTimeLocal = new(9, 0),
            EndTimeLocal = new(17, 0),
            MinimumWorkMinutes = 480
        };
        db.Set<HrShift>().Add(shift);
        await db.SaveChangesAsync();
        var roster = new HrRosterAssignment
        {
            BusinessId = 1,
            EmployeeId = 1,
            WorkDate = workDate,
            ShiftId = shift.Id,
            OperationalLocationProjectionId = 10,
            Status = "Published",
            PublishedByUserId = 101,
            PublishedAtUtc = DateTime.UtcNow,
            ShiftSnapshotJson = "{}",
            LocationSnapshotJson = "{}",
            CalendarEvidenceJson = "{}"
        };
        db.Set<HrRosterAssignment>().Add(roster);
        await db.SaveChangesAsync();
        var attendance = new HrDailyAttendance
        {
            BusinessId = 1,
            EmployeeId = 1,
            WorkDate = workDate,
            OperationalLocationProjectionId = 10,
            SourceLocationTypeSnapshot = "Outlet",
            ShiftId = shift.Id,
            RosterAssignmentId = roster.Id,
            Status = status,
            LastProcessedAtUtc = DateTime.UtcNow,
            ShiftSnapshotJson = "{}",
            LocationSnapshotJson = "{}",
            CalendarEvidenceJson = "{}",
            CalculationFingerprint = new('C', 64)
        };
        db.Set<HrDailyAttendance>().Add(attendance);
        await db.SaveChangesAsync();
        return attendance;
    }

    private async Task AddRosterOnlyAsync(DateOnly workDate)
    {
        var shift = new HrShift
        {
            BusinessId = 1,
            Code = $"PAYROLL-ROSTER-{workDate:yyyyMMdd}",
            Name = "Payroll roster test",
            StartTimeLocal = new(9, 0),
            EndTimeLocal = new(17, 0),
            MinimumWorkMinutes = 480
        };
        db.Set<HrShift>().Add(shift);
        await db.SaveChangesAsync();
        db.Set<HrRosterAssignment>().Add(new HrRosterAssignment
        {
            BusinessId = 1,
            EmployeeId = 1,
            WorkDate = workDate,
            ShiftId = shift.Id,
            OperationalLocationProjectionId = 10,
            Status = "Published",
            PublishedByUserId = 101,
            PublishedAtUtc = DateTime.UtcNow,
            ShiftSnapshotJson = "{}",
            LocationSnapshotJson = "{}",
            CalendarEvidenceJson = "{}"
        });
        await db.SaveChangesAsync();
    }

    private async Task AddEmployeeAsync(
        long id,
        string code,
        DateOnly joiningDate,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        long locationId,
        string status = "Active",
        string assignmentStatus = "Effective")
    {
        db.HrEmployees.Add(new HrEmployee
        {
            Id = id,
            BusinessId = 1,
            EmployeeCode = code,
            FirstName = code,
            DisplayName = code,
            CurrentEmploymentStatus = status,
            CurrentEmploymentTypeId = 1,
            CurrentPrimaryLocationProjectionId = locationId,
            JoiningDate = joiningDate,
            LastWorkingDate = employmentEnd
        });
        await db.SaveChangesAsync();
        db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 1,
            EmployeeId = id,
            EmploymentTypeId = 1,
            StartDate = employmentStart,
            EndDate = employmentEnd,
            Status = employmentEnd.HasValue ? "Closed" : "Active"
        });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1,
            EmployeeId = id,
            OperationalLocationProjectionId = locationId,
            AssignmentType = "Primary",
            EffectiveFrom = employmentStart,
            EffectiveTo = employmentEnd,
            Status = assignmentStatus,
            LocationNameSnapshot = "Location",
            SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet",
            SourceEntityIdSnapshot = locationId.ToString(),
            LocationSourceVersionSnapshot = 1,
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task AddForeignEmployeeAsync()
    {
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent"
        });
        db.HrEmployees.Add(new HrEmployee
        {
            Id = 5, BusinessId = 2, EmployeeCode = "FOREIGN", FirstName = "Foreign",
            DisplayName = "Foreign", CurrentEmploymentStatus = "Active",
            CurrentEmploymentTypeId = 2, CurrentPrimaryLocationProjectionId = 20,
            JoiningDate = new(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 2, EmployeeId = 5, EmploymentTypeId = 2,
            StartDate = new(2026, 1, 1), Status = "Active"
        });
        await db.SaveChangesAsync();
    }

    private async Task AddSalaryAsync(
        long employeeId,
        DateOnly effectiveFrom,
        string valueType = "Fixed",
        bool includeFormula = false,
        decimal baseAmount = 1000m)
    {
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1, Code = $"BASE_{employeeId}", Name = "Base salary",
            ComponentType = "Earning", ValueType = valueType, IsRecurring = true
        };
        var formulaComponent = new PayrollSalaryComponent
        {
            BusinessId = 1,
            Code = $"BONUS_{employeeId}",
            Name = "Calculated bonus",
            ComponentType = "Earning",
            ValueType = "Formula",
            FormulaExpression = $"{component.Code} * 0.1",
            IsRecurring = true,
            CalculationOrder = 2
        };
        var structure = new PayrollSalaryStructure
        {
            BusinessId = 1, Code = $"STRUCT-{employeeId}", Name = "Standard"
        };
        db.PayrollSalaryComponents.Add(component);
        if (includeFormula)
            db.PayrollSalaryComponents.Add(formulaComponent);
        db.PayrollSalaryStructures.Add(structure);
        await db.SaveChangesAsync();

        var policy = new ApprovalPolicy
        {
            BusinessId = 1, Code = $"SALARY-{employeeId}", ProcessType = ApprovalProcessType.SalarySetup,
            Name = "Salary setup", VersionNo = 1, ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2026, 1, 1), IsActive = true
        };
        db.ApprovalPolicies.Add(policy);
        await db.SaveChangesAsync();
        var approval = new ApprovalInstance
        {
            BusinessId = 1, PolicyId = policy.Id, PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = 1, ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = "PayrollSalarySetupRequest", TargetId = employeeId.ToString(),
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business, TargetVersion = 1, RequestHash = new('A', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();
        var request = new PayrollSalarySetupRequest
        {
            BusinessId = 1, EmployeeId = employeeId, SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1, EffectiveFrom = effectiveFrom,
            OperationalLocationProjectionId = 10, StructureSnapshotJson = "{}",
            ComponentSnapshotJson = JsonSerializer.Serialize(
                includeFormula
                    ? new[]
                    {
                        new PayrollSalarySetupComponentSnapshot(
                            component.Id, component.Code, component.Name, component.ComponentType,
                            component.ValueType, null,                             true, 1, null, baseAmount),
                        new PayrollSalarySetupComponentSnapshot(
                            formulaComponent.Id, formulaComponent.Code, formulaComponent.Name,
                            formulaComponent.ComponentType, formulaComponent.ValueType, null,
                            true, formulaComponent.CalculationOrder, formulaComponent.FormulaExpression, null)
                    }
                    : new[]
                    {
                        new PayrollSalarySetupComponentSnapshot(
                            component.Id, component.Code, component.Name, component.ComponentType,
                            component.ValueType, null,                             true, 1, null, baseAmount)
                    },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RequestHash = new('B', 64),
            IdempotencyPayloadHash = new('C', 64), IdempotencyKey = $"setup-{employeeId}",
            Reason = "Test salary setup", Status = "Approved", ApprovalInstanceId = approval.Id,
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow,
            DecidedByUserId = 202, DecidedAtUtc = DateTime.UtcNow, Version = 2
        };
        db.PayrollSalarySetupRequests.Add(request);
        await db.SaveChangesAsync();
        db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
        {
            BusinessId = 1,
            EmployeeId = employeeId,
            SalarySetupRequestId = request.Id,
            SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1,
            EffectiveFrom = effectiveFrom,
            OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}",
            ComponentSnapshotJson = request.ComponentSnapshotJson,
            ApprovalInstanceId = approval.Id,
            ApprovedByUserId = 202,
            ApprovedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
