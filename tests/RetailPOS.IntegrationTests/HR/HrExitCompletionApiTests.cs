using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Controllers.HR;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task CompletionPreviewRequiresFinalSettlementViewWhenPayrollIsEnabled()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, _) = await SeedFinalSettlementExitAsync(factory.Services);

        Authorize(client, "hr.exit.clearance");
        var denied = await client.GetAsync($"/api/hr/v1/exits/{exitCaseId}/completion-preview");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.DoesNotContain("NetSettlement", deniedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OutstandingAmount", deniedBody, StringComparison.OrdinalIgnoreCase);

        SetPermissions(client, "hr.exit.clearance,payroll.final_settlement.view,payroll.salary.view");
        var allowed = await client.GetAsync($"/api/hr/v1/exits/{exitCaseId}/completion-preview");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var preview = (await allowed.Content.ReadFromJsonAsync<HrExitCompletionPreviewModel>())!;
        Assert.True(preview.PayrollEnabled);
        Assert.False(preview.CanComplete);
        Assert.Contains(preview.Blockers,
            blocker => blocker.Contains("approved final settlement is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeniedCompletionHasNoEvidenceOrAuditSideEffects()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, payrollEnabled: false);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);

        Authorize(client, "hr.exit.view");
        var denied = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/complete",
            new HrExitCompleteRequest(exitVersion, new DateOnly(2026, 10, 31), "Denied completion"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.HrExitCompletionEvidence.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row => row.ActionType == "hr.exit.completed").ToArrayAsync());
    }

    [Theory]
    [InlineData("cross_business")]
    [InlineData("unauthorized_location")]
    [InlineData("additional_location_only")]
    public async Task CompletionAuthorizationDenialsHaveNoLifecycleSideEffects(string scenario)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, payrollEnabled: false);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);

        switch (scenario)
        {
            case "cross_business":
                Authorize(client, "hr.exit.clearance");
                client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
                client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
                break;
            case "unauthorized_location":
                await SeedOutletAsync(factory.Services, 20);
                await SeedAdditionalLocationUserAsync(factory.Services);
                Authorize(client, "hr.exit.clearance");
                SetRole(client, "HR");
                SetIdentity(client, 303, "hr.exit.clearance");
                break;
            case "additional_location_only":
                await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
                await SeedAdditionalLocationUserAsync(factory.Services);
                Authorize(client, "hr.exit.clearance");
                SetRole(client, "HR");
                SetIdentity(client, 303, "hr.exit.clearance");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var response = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/complete",
            new HrExitCompleteRequest(exitVersion, new DateOnly(2026, 10, 31), "Denied completion"));
        Assert.Equal(scenario == "cross_business" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
            response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("FinalSettlement", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetSettlement", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CompletionEvidence", responseBody, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var exitCase = await db.HrExitCases.AsNoTracking().SingleAsync(row => row.Id == exitCaseId);
        Assert.Equal("Approved", exitCase.Status);
        Assert.Equal(exitVersion, exitCase.Version);
        Assert.Empty(await db.HrExitCompletionEvidence.ToArrayAsync());
        Assert.Empty(await db.HrEmployeeLifecycleEvents.Where(row => row.EmployeeId == exitCase.EmployeeId)
            .ToArrayAsync());
        Assert.Equal("Active", await db.HrEmployees.Where(row => row.Id == exitCase.EmployeeId)
            .Select(row => row.CurrentEmploymentStatus).SingleAsync());
        Assert.Empty(await db.HrExitCaseEvents.Where(row =>
            row.ExitCaseId == exitCaseId && row.EventType == "Completed").ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row => row.ActionType == "hr.exit.completed").ToArrayAsync());
    }

    [Fact]
    public async Task PayrollDisabledHrOnlyCompletionSucceedsThroughApiWithSafeAudit()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, payrollEnabled: false);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);
        Authorize(client, "hr.exit.clearance");

        var response = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/complete",
            new HrExitCompleteRequest(exitVersion, new DateOnly(2026, 10, 31), "HR clearance complete."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var exitCase = await db.HrExitCases.AsNoTracking().SingleAsync(row => row.Id == exitCaseId);
        Assert.Equal("Completed", exitCase.Status);
        Assert.Equal("Exited", await db.HrEmployees.Where(row => row.Id == exitCase.EmployeeId)
            .Select(row => row.CurrentEmploymentStatus).SingleAsync());
        var evidence = await db.HrExitCompletionEvidence.SingleAsync(row => row.ExitCaseId == exitCaseId);
        Assert.Equal("HrOnly", evidence.CompletionMode);
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row =>
            row.ActionType == "hr.exit.completed");
        Assert.Null(audit.Metadata);
        Assert.DoesNotContain("salary", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bank", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accounting", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BDT", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequiredCompletionAuditFailureRollsBackCompletionAtomically()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, payrollEnabled: false);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);
        Authorize(client, "hr.exit.clearance");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "hr.exit.completed";

        var response = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/complete",
            new HrExitCompleteRequest(exitVersion, new DateOnly(2026, 10, 31), "HR clearance complete."));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var exitCase = await db.HrExitCases.AsNoTracking().SingleAsync(row => row.Id == exitCaseId);
        Assert.Equal("Approved", exitCase.Status);
        Assert.Equal(exitVersion, exitCase.Version);
        Assert.Equal("Active", await db.HrEmployees.Where(row => row.Id == exitCase.EmployeeId)
            .Select(row => row.CurrentEmploymentStatus).SingleAsync());
        Assert.Empty(await db.HrExitCompletionEvidence.ToArrayAsync());
        Assert.Empty(await db.HrEmployeeLifecycleEvents.Where(row => row.EmployeeId == exitCase.EmployeeId)
            .ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row => row.ActionType == "hr.exit.completed").ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentExitCompletionIsSerializedOnPostgres()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await using var database = await PostgresTestDatabase.CreateAsync(
            GetPostgresConnectionString(), PostgresTestDatabasePrefix.PayrollAdjustment);
        await using (var migrationDb = CreatePostgresDb(database.ConnectionString))
            await migrationDb.Database.MigrateAsync();

        using var factory = new FinalSettlementPostgresApiFactory(database.ConnectionString);
        await SeedAsync(factory.Services);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);
        await SeedApprovedCompletionSettlementAsync(factory.Services, exitCaseId, 1000m);

        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        Authorize(clientA, "hr.exit.clearance");
        Authorize(clientB, "hr.exit.clearance");
        var route = $"/api/hr/v1/exits/{exitCaseId}/complete";
        var body = new HrExitCompleteRequest(exitVersion, new DateOnly(2026, 10, 31), "Completion race");
        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync(route, body),
            clientB.PostAsJsonAsync(route, body));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var exitCase = await db.HrExitCases.AsNoTracking().SingleAsync(row => row.Id == exitCaseId);
        Assert.Equal("Completed", exitCase.Status);
        Assert.Equal(exitVersion + 1, exitCase.Version);
        Assert.Equal(1, await db.HrExitCompletionEvidence.CountAsync(row => row.ExitCaseId == exitCaseId));
        Assert.Equal(1, await db.HrExitCaseEvents.CountAsync(row =>
            row.ExitCaseId == exitCaseId && row.EventType == "Completed"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.ActionType == "hr.exit.completed"));
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row =>
            row.ActionType == "hr.exit.completed");
        Assert.Null(audit.Metadata);
        Assert.DoesNotContain("salary", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bank", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accounting", audit.ActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1000", audit.ActionSummary, StringComparison.Ordinal);
    }

    private static async Task SeedApprovedCompletionSettlementAsync(
        IServiceProvider services,
        long exitCaseId,
        decimal netSettlement,
        bool accountingEnabled = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var settlement = new PayrollFinalSettlement
        {
            BusinessId = 1,
            ExitCaseId = exitCaseId,
            EmployeeId = 1,
            CurrentRevisionNo = 1,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101
        };
        db.PayrollFinalSettlements.Add(settlement);
        await db.SaveChangesAsync();

        var revision = new PayrollFinalSettlementRevision
        {
            BusinessId = 1,
            FinalSettlementId = settlement.Id,
            RevisionNo = 1,
            SettlementDate = new DateOnly(2026, 10, 31),
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            GrossDue = netSettlement,
            TotalDeductions = 0m,
            NetSettlement = netSettlement,
            CalculationInputHash = new string('A', 64),
            SnapshotHash = new string('B', 64),
            SnapshotJson = "{}",
            IdempotencyKey = $"completion-{Guid.NewGuid():N}",
            IdempotencyPayloadHash = new string('C', 64),
            CalculatedAtUtc = DateTime.UtcNow,
            CalculatedByUserId = 101,
            Lines =
            [
                new PayrollFinalSettlementLine
                {
                    BusinessId = 1,
                    LineNo = 1,
                    ComponentCode = "NET",
                    ComponentName = "Net settlement",
                    LineType = PayrollFinalSettlementLineTypes.Earning,
                    Amount = netSettlement,
                    IsApplicable = true,
                    SourceType = "PayrollResult",
                    SourceId = "1",
                    EvidenceSnapshotJson = "{}"
                }
            ]
        };
        db.PayrollFinalSettlementRevisions.Add(revision);
        await db.SaveChangesAsync();

        if (!await db.ApprovalPolicies.AnyAsync(row => row.Id == 1))
        {
            db.ApprovalPolicies.Add(new ApprovalPolicy
            {
                Id = 1,
                BusinessId = 1,
                Code = "FINAL-SETTLEMENT",
                ProcessType = ApprovalProcessType.FinalSettlement,
                Name = "Final settlement approval",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Business,
                EffectiveFrom = new DateOnly(2020, 1, 1),
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var approval = new ApprovalInstance
        {
            BusinessId = 1,
            PolicyId = 1,
            PolicyCodeSnapshot = "FINAL-SETTLEMENT",
            PolicyVersionSnapshot = 1,
            AllowSelfApprovalSnapshot = false,
            ProcessType = ApprovalProcessType.FinalSettlement,
            TargetType = "PayrollFinalSettlement",
            TargetId = settlement.Id.ToString(),
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved,
            CurrentStepSequence = 1,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business,
            TargetVersion = 1,
            RequestHash = new string('R', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();

        var submission = new PayrollFinalSettlementApprovalSubmission
        {
            BusinessId = 1,
            FinalSettlementId = settlement.Id,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            EmployeeId = 1,
            ExitCaseId = exitCaseId,
            CalculationSnapshotHash = revision.SnapshotHash,
            ComponentLinesHash = new string('L', 64),
            GrossPayable = revision.GrossDue,
            TotalRecovery = revision.TotalDeductions,
            NetSettlement = revision.NetSettlement,
            CurrencyCode = revision.CurrencyCode,
            SourceEvidenceHash = new string('E', 64),
            CalculationPolicyVersion = "1",
            ClearanceReferencesJson = "[]",
            ClearanceReferencesHash = new string('F', 64),
            SubmitterUserId = 101,
            SubmittedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"submit-{Guid.NewGuid():N}",
            PayloadHash = new string('P', 64),
            SubmittedEvidenceJson = "{}",
            SubmittedEvidenceHash = new string('S', 64)
        };
        db.PayrollFinalSettlementApprovalSubmissions.Add(submission);
        await db.SaveChangesAsync();
        submission.ApprovalInstanceId = approval.Id;
        await db.SaveChangesAsync();
        if (accountingEnabled)
        {
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1,
                FeatureKey = "accounting.enabled",
                IsEnabled = true
            });
            await db.SaveChangesAsync();
        }

        db.ChangeTracker.Clear();

        var payment = new PayrollPayment
        {
            BusinessId = 1,
            FinalSettlementId = settlement.Id,
            FinalSettlementRevisionId = revision.Id,
            FinalSettlementRevisionNo = revision.RevisionNo,
            ApprovalInstanceId = approval.Id,
            HistoricalLocationIdSnapshot = 10,
            DecimalPrecisionSnapshot = 2,
            RoundingModeSnapshot = MidpointRounding.AwayFromZero.ToString(),
            FinalSettlementEvidenceHash = new string('S', 64),
            EmployeeId = 1,
            PayableAmount = netSettlement,
            PaidAmount = netSettlement,
            OutstandingAmount = 0m,
            CurrencyCode = "BDT",
            Method = PayrollPaymentMethods.Cash,
            Status = PayrollPaymentStatuses.Paid,
            IdempotencyKey = $"payment-{Guid.NewGuid():N}",
            IdempotencyPayloadHash = new string('W', 64),
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.PayrollPayments.Add(payment);
        await db.SaveChangesAsync();
        db.PayrollPaymentAllocations.Add(new PayrollPaymentAllocation
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            AllocationNo = 1,
            Method = PayrollPaymentMethods.Cash,
            Amount = netSettlement,
            SettledAmount = netSettlement,
            State = PayrollPaymentAllocationStates.Settled
        });
        await db.SaveChangesAsync();
        var allocation = await db.PayrollPaymentAllocations.SingleAsync(row => row.PayrollPaymentId == payment.Id);
        var evidence = new PayrollPaymentEvidence
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            Type = PayrollPaymentEvidenceTypes.Settlement,
            Amount = netSettlement,
            EvidenceDate = new DateOnly(2026, 10, 31),
            EvidenceReference = "FS-COMPLETE",
            AllocationEvidenceJson = $$"""[{"AllocationNo":1,"Method":"Cash","Amount":{{netSettlement}}}]""",
            IdempotencyKey = $"evidence-{Guid.NewGuid():N}",
            PayloadHash = new string('H', 64),
            VerifiedByUserId = 101,
            VerifiedAtUtc = DateTime.UtcNow
        };
        db.PayrollPaymentEvidence.Add(evidence);
        await db.SaveChangesAsync();
        db.PayrollPaymentReconciliations.Add(new PayrollPaymentReconciliation
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            PayrollPaymentAllocationId = allocation.Id,
            Version = 1,
            PaymentVersion = 1,
            ExpectedAmount = netSettlement,
            SettledAmount = netSettlement,
            Difference = 0m,
            Status = PayrollPaymentReconciliationStatuses.Reconciled,
            SettlementDate = new DateOnly(2026, 10, 31),
            EvidenceSource = PayrollPaymentMethods.Cash,
            ExternalReference = "FS-COMPLETE",
            IdempotencyKey = $"recon-{Guid.NewGuid():N}",
            PayloadHash = new string('Q', 64),
            ReconciledByUserId = 101,
            ReconciledAtUtc = DateTime.UtcNow
        });
        if (accountingEnabled)
        {
            db.PayrollFinalSettlementAccountingPostings.AddRange(
                new PayrollFinalSettlementAccountingPosting
                {
                    BusinessId = 1,
                    FinalSettlementId = settlement.Id,
                    RevisionId = revision.Id,
                    RevisionNo = revision.RevisionNo,
                    PostingType = PayrollFinalSettlementPostingTypes.Expense,
                    PostingReference = "FS-EXPENSE",
                    IdempotencyKey = $"expense-{Guid.NewGuid():N}",
                    RequestPayloadJson = "{}",
                    RequestPayloadHash = new string('X', 64),
                    AccountingJournalId = 9001,
                    Status = PayrollAccountingPostingStatuses.Posted,
                    RequestedByUserId = 101,
                    RequestedAtUtc = DateTime.UtcNow,
                    PostedAtUtc = DateTime.UtcNow
                },
                new PayrollFinalSettlementAccountingPosting
                {
                    BusinessId = 1,
                    FinalSettlementId = settlement.Id,
                    RevisionId = revision.Id,
                    RevisionNo = revision.RevisionNo,
                    PostingType = PayrollFinalSettlementPostingTypes.Payment,
                    PaymentId = payment.Id,
                    PaymentEvidenceId = evidence.Id,
                    PostingReference = "FS-PAYMENT",
                    IdempotencyKey = $"payment-post-{Guid.NewGuid():N}",
                    RequestPayloadJson = "{}",
                    RequestPayloadHash = new string('Y', 64),
                    AccountingJournalId = 9002,
                    Status = PayrollAccountingPostingStatuses.Posted,
                    RequestedByUserId = 101,
                    RequestedAtUtc = DateTime.UtcNow,
                    PostedAtUtc = DateTime.UtcNow
                });
        }

        await db.SaveChangesAsync();
    }
}
