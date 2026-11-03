using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Controllers.HR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task FinalSettlementApprovalFreezesEvidenceExcludesCalculatorAndSupportsReplay()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, _) = await SeedFinalSettlementExitAsync(factory.Services);
        var settlementId = await SeedCalculatedFinalSettlementAsync(factory.Services, exitCaseId);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);
        Authorize(client, "payroll.final_settlement.submit");

        var submitBody = new FinalSettlementSubmitRequest(1, "final-settlement-submit-once");
        var route = $"/api/hr/v1/final-settlements/{settlementId}/submit";
        var first = await client.PostAsJsonAsync(route, submitBody);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var submitted = (await first.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.Equal(ApprovalInstanceStatus.Pending, submitted.Status);
        Assert.False(submitted.IsReplay);

        var replay = await client.PostAsJsonAsync(route, submitBody);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = (await replay.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.True(replayed.IsReplay);
        Assert.Equal(submitted.InstanceId, replayed.InstanceId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.SingleAsync();
        Assert.Equal(101, submission.SubmitterUserId);
        Assert.NotEqual("", submission.SubmittedEvidenceHash);
        var instance = await db.ApprovalInstances.Include(row => row.Steps).SingleAsync();
        Assert.Equal(submission.SubmittedEvidenceHash, instance.RequestHash);
        var candidates = System.Text.Json.JsonSerializer.Deserialize<long[]>(
            instance.Steps.Single().ResolvedApproverCandidatesJson)!;
        Assert.DoesNotContain(101, candidates);
        Assert.Contains(202, candidates);
        Assert.Single(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.final_settlement.requested").ToArrayAsync());

        SetIdentity(client, 202, "payroll.final_settlement.approve");
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{settlementId}/approve",
            new FinalSettlementDecisionRequest(1, "final-settlement-approval-once", "Reviewed"));
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);

        db.ChangeTracker.Clear();
        var approvedModel = await client.GetAsync($"/api/hr/v1/final-settlements/{settlementId}");
        Assert.Equal(HttpStatusCode.Forbidden, approvedModel.StatusCode);
        SetPermissions(client,
            "payroll.final_settlement.view,payroll.salary.view");
        approvedModel = await client.GetAsync($"/api/hr/v1/final-settlements/{settlementId}");
        Assert.Equal(HttpStatusCode.OK, approvedModel.StatusCode);
        var readModel = (await approvedModel.Content.ReadFromJsonAsync<PayrollFinalSettlementModel>())!;
        Assert.Equal("Approved", readModel.Status);
        Assert.Single(readModel.ApprovalHistory!);
    }

    [Fact]
    public async Task FinalSettlementApprovalDenialAndUnresolvedClearanceHaveNoDecisionSideEffects()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, _) = await SeedFinalSettlementExitAsync(factory.Services);
        var settlementId = await SeedCalculatedFinalSettlementAsync(factory.Services, exitCaseId);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);

        Authorize(client, "payroll.final_settlement.submit");
        var submit = await client.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{settlementId}/submit",
            new FinalSettlementSubmitRequest(1, "approval-denial-submit"));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        SetIdentity(client, 202, "payroll.final_settlement.view,payroll.salary.view");
        var denied = await client.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{settlementId}/approve",
            new FinalSettlementDecisionRequest(1, "denied-approval-decision", "Reviewed"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.ApprovalDecisions.ToArrayAsync());
            Assert.Equal(ApprovalInstanceStatus.Pending,
                (await db.ApprovalInstances.SingleAsync()).Status);
            Assert.Empty(await db.AuditEvents.Where(row =>
                row.ActionType == "hr.approval.decided").ToArrayAsync());
        }

        using var clearanceFactory = new SqliteRoleGovernanceApiFactory();
        using var clearanceClient = clearanceFactory.CreateClient();
        await SeedAsync(clearanceFactory.Services);
        var (clearanceExitId, _) = await SeedFinalSettlementExitAsync(clearanceFactory.Services);
        await AddPendingMandatoryClearanceAsync(clearanceFactory.Services, clearanceExitId);
        var clearanceSettlementId = await SeedCalculatedFinalSettlementAsync(
            clearanceFactory.Services, clearanceExitId);
        await SeedFinalSettlementApprovalPolicyAsync(clearanceFactory.Services);
        Authorize(clearanceClient, "payroll.final_settlement.submit");
        var blocked = await clearanceClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{clearanceSettlementId}/submit",
            new FinalSettlementSubmitRequest(1, "unresolved-clearance-submit"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using var clearanceScope = clearanceFactory.Services.CreateScope();
        var clearanceDb = clearanceScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await clearanceDb.PayrollFinalSettlementApprovalSubmissions.ToArrayAsync());
        Assert.Empty(await clearanceDb.ApprovalInstances
            .Where(row => row.ProcessType == ApprovalProcessType.FinalSettlement).ToArrayAsync());
        Assert.Empty(await clearanceDb.AuditEvents.Where(row =>
            row.ActionType == "payroll.final_settlement.requested").ToArrayAsync());
    }

    private const string FinalSettlementPermissions =
        "payroll.final_settlement.view,payroll.final_settlement.calculate";

    [Fact]
    public async Task FinalSettlementExecutionRequiresDedicatedFinancialAndAccountingPermissions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, _) = await SeedFinalSettlementExitAsync(factory.Services);
        var settlementId = await SeedCalculatedFinalSettlementAsync(factory.Services, exitCaseId);
        Authorize(client, "payroll.final_settlement.view,payroll.salary.view");

        var executionRoute = $"/api/hr/v1/final-settlements/{settlementId}/execution";
        var deniedRead = await client.GetAsync(executionRoute);
        Assert.True(deniedRead.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403, got {(int)deniedRead.StatusCode}: "
            + await deniedRead.Content.ReadAsStringAsync());

        SetPermissions(client, "payroll.final_settlement.financial.view,payroll.salary.view");
        var authorizedRead = await client.GetAsync(executionRoute);
        Assert.Equal(HttpStatusCode.OK, authorizedRead.StatusCode);
        var status = (await authorizedRead.Content
            .ReadFromJsonAsync<PayrollFinalSettlementExecutionStatus>())!;
        Assert.Equal(settlementId, status.FinalSettlementId);
        Assert.Equal("Calculated", status.SettlementStatus);
        Assert.Empty(status.Payments);
        Assert.Empty(status.AccountingPostings);

        var deniedPost = await client.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{settlementId}/accounting",
            new FinalSettlementAccountingPostRequest(1, "denied-accounting-post"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedPost.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollFinalSettlementAccountingPostings.ToArrayAsync());
        Assert.Empty(await db.JournalEntries.Where(row =>
            row.ReferenceType == "PayrollFinalSettlement").ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.final_settlement.accounting.posting_requested").ToArrayAsync());
    }

    [Fact]
    public async Task FinalSettlementEndpointsEnforceSalaryPermissionAndBusinessIsolation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);
        Authorize(client, FinalSettlementPermissions);

        var calculateResponse = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/final-settlement/calculate",
            new HrFinalSettlementCalculateRequest(exitVersion, "final-settlement-api-test"));
        Assert.Equal(HttpStatusCode.OK, calculateResponse.StatusCode);
        var settlement = (await calculateResponse.Content
            .ReadFromJsonAsync<PayrollFinalSettlementModel>())!;

        var exitReadRoute = $"/api/hr/v1/exits/{exitCaseId}/final-settlement";
        var settlementReadRoute = $"/api/hr/v1/final-settlements/{settlement.Id}";
        var confidentialDenied = await client.GetAsync(exitReadRoute);
        Assert.Equal(HttpStatusCode.Forbidden, confidentialDenied.StatusCode);
        var deniedBody = await confidentialDenied.Content.ReadAsStringAsync();
        Assert.DoesNotContain("NetSettlement", deniedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GrossDue", deniedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(settlementReadRoute)).StatusCode);

        SetPermissions(client, $"{FinalSettlementPermissions},payroll.salary.view");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(exitReadRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(settlementReadRoute)).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(settlementReadRoute)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.PayrollFinalSettlements.ToArrayAsync());
        Assert.Single(await db.PayrollFinalSettlementRevisions.ToArrayAsync());
        Assert.Single(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.final_settlement.calculated").ToArrayAsync());
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row =>
            row.ActionType == "payroll.final_settlement.calculated");
        Assert.Null(audit.Metadata);
        Assert.DoesNotContain(settlement.GrossDue.ToString(), audit.ActionSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(settlement.NetSettlement.ToString(), audit.ActionSummary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing_permission")]
    [InlineData("payroll_disabled")]
    [InlineData("location_scope")]
    [InlineData("additional_location_only")]
    public async Task DeniedFinalSettlementCalculationHasNoFinancialSideEffects(string scenario)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(factory.Services);

        switch (scenario)
        {
            case "missing_permission":
                Authorize(client, "payroll.final_settlement.view,payroll.salary.view");
                break;
            case "payroll_disabled":
                Authorize(client, FinalSettlementPermissions);
                await SetPayrollEnabledAsync(factory.Services, false);
                break;
            case "location_scope":
                await SeedOutletAsync(factory.Services, 20);
                await SeedAdditionalLocationUserAsync(factory.Services);
                Authorize(client, FinalSettlementPermissions);
                SetRole(client, "HR");
                SetIdentity(client, 303, FinalSettlementPermissions);
                break;
            case "additional_location_only":
                await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
                await SeedAdditionalLocationUserAsync(factory.Services);
                Authorize(client, FinalSettlementPermissions);
                SetRole(client, "HR");
                SetIdentity(client, 303, FinalSettlementPermissions);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var response = await client.PostAsJsonAsync(
            $"/api/hr/v1/exits/{exitCaseId}/final-settlement/calculate",
            new HrFinalSettlementCalculateRequest(exitVersion, $"denied-{scenario}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("GrossDue", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetSettlement", responseBody, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollFinalSettlements.ToArrayAsync());
        Assert.Empty(await db.PayrollFinalSettlementRevisions.ToArrayAsync());
        Assert.Empty(await db.PayrollFinalSettlementLines.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.final_settlement.calculated").ToArrayAsync());
    }

    private static async Task<(long ExitCaseId, long ExitVersion)> SeedFinalSettlementExitAsync(
        IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var template = new HrClearanceTemplate
        {
            BusinessId = 1,
            Code = $"FSA-{Guid.NewGuid():N}",
            Name = "Final settlement API test",
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
            Reason = "Final settlement API authorization test",
            Status = "Approved",
            InitiatedByUserId = 101,
            InitiatedAtUtc = DateTime.UtcNow,
            PrimaryLocationProjectionIdSnapshot = 10,
            ClearanceTemplateId = template.Id,
            ClearanceTemplateVersionNo = 1,
            ClearanceTemplateSnapshotJson = "{}",
            Version = 3
        };
        db.HrExitCases.Add(exitCase);
        await db.SaveChangesAsync();
        return (exitCase.Id, exitCase.Version);
    }

    private static async Task<long> SeedCalculatedFinalSettlementAsync(
        IServiceProvider services, long exitCaseId)
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
            SettlementDate = new(2026, 10, 31),
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            GrossDue = 1200m,
            TotalDeductions = 200m,
            NetSettlement = 1000m,
            CalculationInputHash = new string('A', 64),
            SnapshotHash = new string('B', 64),
            SnapshotJson = "{}",
            IdempotencyKey = $"approval-seed-{Guid.NewGuid():N}",
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
                    ComponentName = "Net payable",
                    LineType = PayrollFinalSettlementLineTypes.Earning,
                    Amount = 1000m,
                    IsApplicable = true,
                    SourceType = "PayrollResult",
                    SourceId = "1",
                    EvidenceSnapshotJson = "{}"
                }
            ]
        };
        db.PayrollFinalSettlementRevisions.Add(revision);
        await db.SaveChangesAsync();
        return settlement.Id;
    }

    private static async Task AddPendingMandatoryClearanceAsync(
        IServiceProvider services, long exitCaseId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.HrClearanceItems.Add(new HrClearanceItem
        {
            BusinessId = 1,
            ExitCaseId = exitCaseId,
            Sequence = 1,
            Code = "RETURN-ASSETS",
            Name = "Return assets",
            IsMandatory = true,
            Status = "Pending",
            Version = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedFinalSettlementApprovalPolicyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "FINAL-SETTLEMENT-APPROVAL-API",
            ProcessType = ApprovalProcessType.FinalSettlement,
            Name = "Final settlement approval",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2020, 1, 1),
            IsActive = true
        };
        policy.Steps.Add(new ApprovalPolicyStep
        {
            Sequence = 1,
            ApproverType = ApprovalApproverType.User,
            ApproverReference = "202",
            RequiredApprovals = 1,
            CanReject = true,
            CanCancel = true
        });
        db.ApprovalPolicies.Add(policy);
        db.Users.Add(new User
        {
            Id = 202,
            BusinessId = 1,
            Name = "Final settlement approver",
            Email = "final-settlement-approver@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        await db.SaveChangesAsync();
    }

    private static async Task SetPayrollEnabledAsync(IServiceProvider services, bool enabled)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var setting = await db.BusinessFeatureSettings.SingleAsync(row =>
            row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
        setting.IsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    private static async Task SeedOutletAsync(IServiceProvider services, long outletId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Outlets.Add(new RetailPOS.Core.Entities.Outlet
        {
            Id = outletId, BusinessId = 1, Name = "Unauthorized outlet"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdditionalLocationUserAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Users.Add(new RetailPOS.Core.Entities.User
        {
            Id = 303,
            BusinessId = 1,
            Name = "Additional-location-only user",
            Email = "final-settlement-location@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            OutletId = 20,
            InventoryLocationAccessScope = RetailPOS.Core.Entities.User.InventoryAccessAssignedOnly
        });
        db.UserOutletAssignments.Add(new RetailPOS.Core.Entities.UserOutletAssignment
        {
            UserId = 303,
            BusinessId = 1,
            OutletId = 20,
            IsPrimary = true
        });
        await db.SaveChangesAsync();
    }
}
