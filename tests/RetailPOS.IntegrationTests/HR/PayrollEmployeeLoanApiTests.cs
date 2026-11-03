using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Controllers.Payroll;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollEmployeeLoanApiTests
{
    [Fact]
    public void LoanRoutesAppearInOpenApi()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        var paths = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1").Paths.Keys;
        Assert.Contains("/api/payroll/v1/loans", paths);
        Assert.Contains("/api/payroll/v1/loans/{id}", paths);
        Assert.Contains("/api/payroll/v1/loans/{id}/repayments", paths);
        Assert.Contains("/api/payroll/v1/loans/{id}/schedule", paths);
    }

    [Fact]
    public async Task LoanApprovalDisbursementScopeScheduleRepaymentAndAuditAreEnforced()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        using var maker = factory.CreateClient();
        Auth(maker, "payroll.loan.view,payroll.loan.manage,payroll.loan.approve");
        using var unauthorizedLocation = factory.CreateClient();
        Auth(unauthorizedLocation, "payroll.loan.view,payroll.loan.manage", 303);
        using var foreignBusiness = factory.CreateClient();
        Auth(foreignBusiness, "payroll.loan.view", 201, 2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recoveryStartDate = today < new DateOnly(2026, 1, 1)
            ? new DateOnly(2026, 1, 1)
            : today;
        var request = new CreatePayrollEmployeeLoanRequest(
            1, "Loan", 1000m, recoveryStartDate, "FixedMonthly", 500m, [],
            "Emergency loan", "REF-1", "loan-once");

        factory.RoleAuditFailureInterceptor.FailPayrollLoanAudit = true;
        using var failedAuditCreation = await maker.PostAsJsonAsync(
            "/api/payroll/v1/loans", request with { IdempotencyKey = "loan-audit-failure" });
        Assert.Equal(HttpStatusCode.InternalServerError, failedAuditCreation.StatusCode);
        factory.RoleAuditFailureInterceptor.FailPayrollLoanAudit = false;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollEmployeeLoans.ToArrayAsync());
        }

        using var missingManage = factory.CreateClient();
        Auth(missingManage, "payroll.loan.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingManage.PostAsJsonAsync("/api/payroll/v1/loans", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await unauthorizedLocation.PostAsJsonAsync("/api/payroll/v1/loans", request)).StatusCode);

        using var createdResponse = await maker.PostAsJsonAsync("/api/payroll/v1/loans", request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        Assert.Equal("Draft", created.Status);
        Assert.Null(created.ApprovedAmount);
        Assert.Null(created.DisbursedDate);
        Assert.Equal(0m, created.OutstandingAmount);
        Assert.Empty(created.ScheduleHistory);

        using var replayResponse = await maker.PostAsJsonAsync(
            "/api/payroll/v1/loans", request);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.True((await replayResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict,
            (await maker.PostAsJsonAsync("/api/payroll/v1/loans",
                request with { PrincipalAmount = 1200m })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await unauthorizedLocation.GetAsync($"/api/payroll/v1/loans/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignBusiness.GetAsync($"/api/payroll/v1/loans/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingManage.PostAsJsonAsync($"/api/payroll/v1/loans/{created.Id}/submit",
                new PayrollLoanVersionRequest(created.Version))).StatusCode);

        using var submitResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/submit",
            new PayrollLoanVersionRequest(created.Version));
        Assert.True(submitResponse.StatusCode == HttpStatusCode.OK,
            await submitResponse.Content.ReadAsStringAsync());
        var submitted = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{created.Id}"))!;
        Assert.Equal("PendingApproval", submitted.Status);
        Assert.NotNull(submitted.ApprovalInstanceId);
        Assert.Equal(0m, submitted.OutstandingAmount);

        using var selfApprovalResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{submitted.ApprovalInstanceId}/approve",
            new PayrollLoanDecisionRequest("self-approval", "Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, selfApprovalResponse.StatusCode);

        using var approver = factory.CreateClient();
        Auth(approver, "payroll.loan.view,payroll.loan.approve", 202);
        factory.RoleAuditFailureInterceptor.FailApprovalDecisionAudit = true;
        using var failedApprovalAudit = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{submitted.ApprovalInstanceId}/approve",
            new PayrollLoanDecisionRequest("approve-loan", "Approved"));
        Assert.Equal(HttpStatusCode.InternalServerError, failedApprovalAudit.StatusCode);
        factory.RoleAuditFailureInterceptor.FailApprovalDecisionAudit = false;
        var stillPending = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{created.Id}"))!;
        Assert.Equal("PendingApproval", stillPending.Status);
        Assert.Equal(0m, stillPending.OutstandingAmount);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.ApprovalDecisions.ToArrayAsync());
        }
        using var approvalResponse = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{submitted.ApprovalInstanceId}/approve",
            new PayrollLoanDecisionRequest("approve-loan", "Approved"));
        Assert.True(approvalResponse.StatusCode == HttpStatusCode.OK,
            await approvalResponse.Content.ReadAsStringAsync());
        var approvalReplay = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{submitted.ApprovalInstanceId}/approve",
            new PayrollLoanDecisionRequest("approve-loan", "Approved"));
        Assert.Equal(HttpStatusCode.OK, approvalReplay.StatusCode);
        Assert.True((await approvalReplay.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!.IsReplay);

        var active = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{created.Id}"))!;
        Assert.Equal("Active", active.Status);
        Assert.Equal(1000m, active.ApprovedAmount);
        Assert.Equal(1000m, active.OutstandingAmount);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), active.DisbursedDate);
        Assert.Single(active.ScheduleHistory);
        Assert.Equal(2, active.ScheduleHistory[0].Lines.Count);

        using var futureRepayment = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/repayments",
            new RecordPayrollLoanRepaymentRequest(
                100m, today.AddDays(1), "Future repayment", null, "future-payment"));
        Assert.Equal(HttpStatusCode.BadRequest, futureRepayment.StatusCode);

        var repayment = new RecordPayrollLoanRepaymentRequest(
            500m, today, "Partial repayment", "PAY-1", "repayment-once");
        using var partialResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/repayments", repayment);
        Assert.True(partialResponse.StatusCode == HttpStatusCode.OK,
            await partialResponse.Content.ReadAsStringAsync());
        var partiallyPaid = (await partialResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        Assert.Equal(500m, partiallyPaid.OutstandingAmount);
        Assert.Equal(500m, partiallyPaid.ScheduleHistory[0].Lines.Sum(line => line.ActualRecoveredAmount));

        using var repaymentReplay = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/repayments", repayment);
        Assert.Equal(HttpStatusCode.OK, repaymentReplay.StatusCode);
        Assert.True((await repaymentReplay.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict,
            (await maker.PostAsJsonAsync($"/api/payroll/v1/loans/{created.Id}/repayments",
                repayment with { Amount = 400m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await maker.PostAsJsonAsync($"/api/payroll/v1/loans/{created.Id}/repayments",
                repayment with { Amount = 501m, IdempotencyKey = "over-repay" })).StatusCode);

        using var scheduleResponse = await maker.PutAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/schedule",
            new UpdatePayrollLoanScheduleRequest(
                partiallyPaid.Version, recoveryStartDate.AddMonths(1), "CustomSchedule", null,
                [new(recoveryStartDate.AddMonths(1), 250m), new(recoveryStartDate.AddMonths(2), 250m)], "schedule-v2"));
        Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);
        var rescheduled = (await scheduleResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        Assert.Equal(2, rescheduled.ScheduleHistory.Count);
        Assert.Equal("Superseded", rescheduled.ScheduleHistory[0].Status);
        Assert.Equal("Current", rescheduled.ScheduleHistory[1].Status);
        using var scheduleReplay = await maker.PutAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/schedule",
            new UpdatePayrollLoanScheduleRequest(
                partiallyPaid.Version, recoveryStartDate.AddMonths(1), "CustomSchedule", null,
                [new(recoveryStartDate.AddMonths(1), 250m), new(recoveryStartDate.AddMonths(2), 250m)],
                "schedule-v2"));
        Assert.Equal(HttpStatusCode.OK, scheduleReplay.StatusCode);
        Assert.True((await scheduleReplay.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict,
            (await maker.PutAsJsonAsync(
                $"/api/payroll/v1/loans/{created.Id}/schedule",
                new UpdatePayrollLoanScheduleRequest(
                    partiallyPaid.Version, recoveryStartDate.AddMonths(1), "CustomSchedule", null,
                    [new(recoveryStartDate.AddMonths(1), 300m), new(recoveryStartDate.AddMonths(2), 200m)],
                    "schedule-v2"))).StatusCode);

        using var settlementResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/repayments",
            new RecordPayrollLoanRepaymentRequest(
                500m, today, "Early settlement", "PAY-2", "repayment-settle"));
        Assert.Equal(HttpStatusCode.OK, settlementResponse.StatusCode);
        var settled = (await settlementResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        Assert.Equal("Settled", settled.Status);
        Assert.Equal(0m, settled.OutstandingAmount);

        var allLoans = await maker.GetFromJsonAsync<IReadOnlyList<PayrollEmployeeLoanResponse>>(
            "/api/payroll/v1/loans?employeeId=1");
        Assert.Single(allLoans!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var loanAudits = await db.AuditEvents.Where(item => item.ActionType.StartsWith("payroll.loan."))
                .ToArrayAsync();
            Assert.Contains(loanAudits, item => item.ActionType == "payroll.loan.created");
            Assert.Contains(loanAudits, item => item.ActionType == "payroll.loan.accessed");
            Assert.Contains(loanAudits, item => item.ActionType == "payroll.loan.repayment_recorded");
            Assert.All(loanAudits, item => Assert.DoesNotContain("1000", item.ActionSummary));
        }
    }

    [Fact]
    public async Task RejectedLoanDoesNotDisburseAndHrCapabilityIsRequired()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        using var maker = factory.CreateClient();
        Auth(maker, "payroll.loan.view,payroll.loan.manage");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recoveryStartDate = today < new DateOnly(2026, 1, 1)
            ? new DateOnly(2026, 1, 1)
            : today;
        var request = new CreatePayrollEmployeeLoanRequest(
            1, "Advance", 250m, recoveryStartDate, "Manual", null, [],
            "Short advance", null, "advance-reject");
        using var createdResponse = await maker.PostAsJsonAsync("/api/payroll/v1/loans", request);
        Assert.True(createdResponse.StatusCode == HttpStatusCode.Created,
            await createdResponse.Content.ReadAsStringAsync());
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        Assert.True(created.Id > 0);
        Assert.True(created.Version > 0);
        using var submittedResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{created.Id}/submit",
            new PayrollLoanVersionRequest(created.Version));
        Assert.True(submittedResponse.StatusCode == HttpStatusCode.OK,
            await submittedResponse.Content.ReadAsStringAsync());
        var pending = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{created.Id}"))!;

        using var approver = factory.CreateClient();
        Auth(approver, "payroll.loan.view,payroll.loan.approve", 202);
        using var rejected = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{pending.ApprovalInstanceId}/reject",
            new PayrollLoanDecisionRequest("reject-advance", "Rejected"));
        Assert.True(rejected.StatusCode == HttpStatusCode.OK,
            await rejected.Content.ReadAsStringAsync());
        var result = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{created.Id}"))!;
        Assert.Equal("Rejected", result.Status);
        Assert.Null(result.ApprovedAmount);
        Assert.Null(result.DisbursedDate);
        Assert.Equal(0m, result.OutstandingAmount);

        var cancellableRequest = request with { IdempotencyKey = "advance-cancel" };
        using var cancellableResponse = await maker.PostAsJsonAsync(
            "/api/payroll/v1/loans", cancellableRequest);
        Assert.Equal(HttpStatusCode.Created, cancellableResponse.StatusCode);
        var cancellable = (await cancellableResponse.Content.ReadFromJsonAsync<PayrollEmployeeLoanResponse>())!;
        using var cancellableSubmit = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/{cancellable.Id}/submit",
            new PayrollLoanVersionRequest(cancellable.Version));
        Assert.Equal(HttpStatusCode.OK, cancellableSubmit.StatusCode);
        var cancellablePending = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{cancellable.Id}"))!;
        using var cancellation = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/loans/approvals/{cancellablePending.ApprovalInstanceId}/cancel",
            new PayrollLoanDecisionRequest("cancel-advance", "Withdrawn"));
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        var cancelled = (await maker.GetFromJsonAsync<PayrollEmployeeLoanResponse>(
            $"/api/payroll/v1/loans/{cancellable.Id}"))!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Null(cancelled.ApprovedAmount);
        Assert.Null(cancelled.DisbursedDate);
        Assert.Equal(0m, cancelled.OutstandingAmount);

        using var disabledHrFactory = new SqliteRoleGovernanceApiFactory();
        await Seed(disabledHrFactory.Services, hrEnabled: false);
        using var disabledHrClient = disabledHrFactory.CreateClient();
        Auth(disabledHrClient, "payroll.loan.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabledHrClient.GetAsync("/api/payroll/v1/loans?employeeId=1")).StatusCode);

        using var disabledPayrollFactory = new SqliteRoleGovernanceApiFactory();
        await Seed(disabledPayrollFactory.Services, payrollEnabled: false);
        using var disabledPayrollClient = disabledPayrollFactory.CreateClient();
        Auth(disabledPayrollClient, "payroll.loan.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabledPayrollClient.GetAsync("/api/payroll/v1/loans?employeeId=1")).StatusCode);
    }

    private static async Task Seed(
        IServiceProvider services, bool hrEnabled = true, bool payrollEnabled = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Loans" },
            new Business { Id = 2, Name = "Foreign loans" });
        foreach (var businessId in new long[] { 1, 2 })
        {
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = businessId, FeatureKey = "hr.enabled",
                    IsEnabled = businessId == 1 ? hrEnabled : true
                },
                new BusinessFeatureSetting
                {
                    BusinessId = businessId, FeatureKey = "payroll.enabled",
                    IsEnabled = businessId == 1 ? payrollEnabled : true
                });
        }
        await db.SaveChangesAsync();
        await HrRosterPersistenceTests.Seed(db, businessExists: true);

        db.Outlets.Add(new Outlet { Id = 20, BusinessId = 1, Name = "Other Outlet" });
        db.OperationalLocationProjections.Add(new OperationalLocationProjection
        {
            Id = 21, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
            SourceEntityId = "20", Name = "Other Outlet", SourceVersion = 1
        });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 21,
            AssignmentType = "Additional", EffectiveFrom = new DateOnly(2026, 1, 1),
            LocationNameSnapshot = "Other Outlet", SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "20",
            LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
        });
        db.Users.AddRange(
            CreateUser(101, 1, "Loan maker", 10),
            CreateUser(202, 1, "Loan approver", 10),
            CreateUser(303, 1, "Other location", 20),
            CreateUser(201, 2, "Foreign user", 20));
        db.UserOutletAssignments.AddRange(
            UserAssignment(101, 1, 10), UserAssignment(202, 1, 10),
            UserAssignment(303, 1, 20), UserAssignment(201, 2, 20));
        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "LOAN_API_TEST",
            ProcessType = ApprovalProcessType.EmployeeLoan,
            Name = "Deterministic loan approval",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Location,
            ScopeReference = "10",
            EffectiveFrom = new DateOnly(2000, 1, 1),
            IsActive = true,
            Steps =
            [
                new ApprovalPolicyStep
                {
                    Sequence = 1,
                    ApproverType = ApprovalApproverType.User,
                    ApproverReference = "202",
                    RequiredApprovals = 1,
                    CanReject = true,
                    CanCancel = true
                }
            ]
        };
        db.ApprovalPolicies.Add(policy);
        await db.SaveChangesAsync();
    }

    private static User CreateUser(long id, long businessId, string name, long outletId) =>
        new()
        {
            Id = id, BusinessId = businessId, Name = name,
            Email = $"{id}@example.invalid", PasswordHash = "test", IsActive = true,
            OutletId = outletId, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
        };

    private static UserOutletAssignment UserAssignment(long userId, long businessId, long outletId) =>
        new() { UserId = userId, BusinessId = businessId, OutletId = outletId, IsPrimary = true };

    private static void Auth(
        HttpClient client, string permissions, long userId = 101, long businessId = 1)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
