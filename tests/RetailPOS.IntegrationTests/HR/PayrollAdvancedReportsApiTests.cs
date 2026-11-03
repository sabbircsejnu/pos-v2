using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task AdvancedPayrollReportsRequireReportAndSalaryPermissionsAndUsePersistedEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var salaryVersionId = await db.PayrollEmployeeSalaryVersions
                .Where(row => row.BusinessId == 1 && row.EmployeeId == 1)
                .Select(row => row.Id)
                .SingleAsync();
            db.PayrollRuns.AddRange(
                new PayrollRun
                {
                    Id = 901, BusinessId = 1, PayrollPeriodId = 1, RunNo = 1,
                    RunType = PayrollRunTypes.Regular, ScopeType = PayrollRunScopeTypes.Business,
                    ScopeKey = PayrollRunScopeTypes.BusinessKey, Status = PayrollRunStatuses.Completed,
                    CurrencyCode = "BDT", DecimalPrecision = 2, RoundingMode = "AwayFromZero",
                    CalculationVersion = 1, IdempotencyKey = "advanced-report-regular",
                    IdempotencyPayloadHash = new string('A', 64), CreatedByUserId = 101,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new PayrollRun
                {
                    Id = 902, BusinessId = 1, PayrollPeriodId = 1, RunNo = 2,
                    RunType = PayrollRunTypes.OffCycle, RunReference = "ADV-OC-1",
                    ScopeType = PayrollRunScopeTypes.Business, ScopeKey = PayrollRunScopeTypes.BusinessKey,
                    Status = PayrollRunStatuses.Completed, CurrencyCode = "BDT", DecimalPrecision = 2,
                    RoundingMode = "AwayFromZero", CalculationVersion = 1,
                    IdempotencyKey = "advanced-report-offcycle",
                    IdempotencyPayloadHash = new string('B', 64), CreatedByUserId = 101,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new PayrollRun
                {
                    Id = 903, BusinessId = 1, PayrollPeriodId = 1, RunNo = 3,
                    RunType = PayrollRunTypes.Regular, RunReference = "ADV-R2",
                    ScopeType = PayrollRunScopeTypes.Location, ScopeId = 11,
                    ScopeKey = PayrollRunScopeTypes.LocationKey(11),
                    Status = PayrollRunStatuses.Completed, CurrencyCode = "BDT", DecimalPrecision = 2,
                    RoundingMode = "AwayFromZero", CalculationVersion = 1,
                    IdempotencyKey = "advanced-report-regular-2",
                    IdempotencyPayloadHash = new string('Z', 64), CreatedByUserId = 101,
                    CreatedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            db.PayrollEmployeeResults.AddRange(
                new PayrollEmployeeResult
                {
                    Id = 901, BusinessId = 1, PayrollRunId = 901, CalculationVersion = 1,
                    CalculationInputHash = new string('C', 64), EmployeeId = 1,
                    EmployeeCodeSnapshot = "EMP-1", EmployeeNameSnapshot = "Report Employee",
                    LocationIdSnapshot = 10, SalaryVersionId = salaryVersionId,
                    SalaryEffectiveFromSnapshot = new DateOnly(2026, 1, 1),
                    GrossEarnings = 1200m, TotalDeductions = 200m, NetPay = 1000m,
                    Status = PayrollEmployeeResultStatuses.Calculated, CalculatedAtUtc = DateTime.UtcNow
                },
                new PayrollEmployeeResult
                {
                    Id = 902, BusinessId = 1, PayrollRunId = 902, CalculationVersion = 1,
                    CalculationInputHash = new string('D', 64), EmployeeId = 1,
                    EmployeeCodeSnapshot = "EMP-1", EmployeeNameSnapshot = "Report Employee",
                    LocationIdSnapshot = 10, SalaryVersionId = salaryVersionId,
                    SalaryEffectiveFromSnapshot = new DateOnly(2026, 1, 1),
                    GrossEarnings = 300m, TotalDeductions = 50m, NetPay = 250m,
                    Status = PayrollEmployeeResultStatuses.Calculated, CalculatedAtUtc = DateTime.UtcNow
                },
                new PayrollEmployeeResult
                {
                    Id = 903, BusinessId = 1, PayrollRunId = 903, CalculationVersion = 1,
                    CalculationInputHash = new string('H', 64), EmployeeId = 1,
                    EmployeeCodeSnapshot = "EMP-1", EmployeeNameSnapshot = "Factory Employee",
                    LocationIdSnapshot = 11, SalaryVersionId = salaryVersionId,
                    SalaryEffectiveFromSnapshot = new DateOnly(2026, 1, 1),
                    GrossEarnings = 800m, TotalDeductions = 100m, NetPay = 700m,
                    Status = PayrollEmployeeResultStatuses.Calculated, CalculatedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            db.PayrollEmployeeResultLines.AddRange(
                new PayrollEmployeeResultLine
                {
                    Id = 901, BusinessId = 1, PayrollEmployeeResultId = 901,
                    ComponentCodeSnapshot = "BASE", ComponentNameSnapshot = "Base salary",
                    LineType = "Earning", Amount = 1200m, SourceType = "Salary"
                },
                new PayrollEmployeeResultLine
                {
                    Id = 902, BusinessId = 1, PayrollEmployeeResultId = 901,
                    ComponentCodeSnapshot = "TAX", ComponentNameSnapshot = "Tax",
                    LineType = "Deduction", Amount = 200m, SourceType = "TaxStatutory"
                },
                new PayrollEmployeeResultLine
                {
                    Id = 903, BusinessId = 1, PayrollEmployeeResultId = 901,
                    ComponentCodeSnapshot = "PERSONAL", ComponentNameSnapshot = "Loan recovery",
                    LineType = "Deduction", Amount = 75m, SourceType = "LoanRecovery",
                    SourceId = "990:1:1"
                },
                new PayrollEmployeeResultLine
                {
                    Id = 904, BusinessId = 1, PayrollEmployeeResultId = 903,
                    ComponentCodeSnapshot = "BASE", ComponentNameSnapshot = "Base salary",
                    LineType = "Earning", Amount = 800m, SourceType = "Salary"
                });
            if (!await db.OperationalLocationProjections.AnyAsync(row => row.BusinessId == 1 && row.Id == 11))
            {
                db.OperationalLocationProjections.Add(new OperationalLocationProjection
                {
                    Id = 11,
                    BusinessId = 1,
                    SourceSystem = "seed",
                    SourceEntityType = "Warehouse",
                    SourceEntityId = "11",
                    Name = "Factory location",
                    Address = "Factory address",
                    SourceUpdatedAtUtc = DateTime.UtcNow,
                    SourceVersion = 1,
                    SourcePayloadHash = new string('Q', 64),
                    LastSyncedAtUtc = DateTime.UtcNow
                });
            }
            db.PayrollEmployeeLoans.Add(new PayrollEmployeeLoan
            {
                Id = 990, BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 10,
                LoanType = "Personal", PrincipalAmount = 200m, ApprovedAmount = 200m,
                RecoveryStartDate = new DateOnly(2026, 10, 1), RecoveryMode = "Manual",
                OutstandingAmount = 175m, ProposedScheduleJson = "[]",
                RequestHash = new string('E', 64), IdempotencyKey = "advanced-report-loan",
                IdempotencyPayloadHash = new string('F', 64), Reason = "Report fixture",
                Status = "Active", CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
            });
            db.PayrollLoanRepayments.Add(new PayrollLoanRepayment
            {
                Id = 990, BusinessId = 1, LoanId = 990, Amount = 25m,
                PaymentDate = new DateOnly(2026, 10, 15), Reason = "Manual repayment",
                IdempotencyKey = "advanced-report-repayment",
                IdempotencyPayloadHash = new string('G', 64), RecordedByUserId = 101,
                RecordedAtUtc = DateTime.UtcNow
            });
            db.PayrollPayments.AddRange(
                new PayrollPayment
                {
                    Id = 910, BusinessId = 1, PayrollPeriodId = 1, PayrollRunId = 901, PayrollEmployeeResultId = 901,
                    EmployeeId = 1, HistoricalLocationIdSnapshot = 10,
                    PayableAmount = 1000m, PaidAmount = 700m, OutstandingAmount = 300m,
                    CurrencyCode = "BDT", Method = PayrollPaymentMethods.Cash,
                    Status = PayrollPaymentStatuses.PartiallyPaid, AttemptNo = 1,
                    IdempotencyKey = "advanced-report-payment-1",
                    IdempotencyPayloadHash = new string('J', 64),
                    CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
                },
                new PayrollPayment
                {
                    Id = 911, BusinessId = 1, PayrollPeriodId = 1, PayrollRunId = 901, PayrollEmployeeResultId = 901,
                    EmployeeId = 1, HistoricalLocationIdSnapshot = 10,
                    PayableAmount = 1000m, PaidAmount = 700m, OutstandingAmount = 300m,
                    CurrencyCode = "BDT", Method = PayrollPaymentMethods.Bank,
                    Status = PayrollPaymentStatuses.Returned, AttemptNo = 2,
                    IdempotencyKey = "advanced-report-payment-returned",
                    IdempotencyPayloadHash = new string('K', 64),
                    CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
                },
                new PayrollPayment
                {
                    Id = 912, BusinessId = 1, PayrollPeriodId = 1, PayrollRunId = 901, PayrollEmployeeResultId = 901,
                    OriginalPaymentId = 911,
                    EmployeeId = 1, HistoricalLocationIdSnapshot = 10,
                    PayableAmount = 1000m, PaidAmount = 1000m, OutstandingAmount = 0m,
                    CurrencyCode = "BDT", Method = PayrollPaymentMethods.Bank,
                    Status = PayrollPaymentStatuses.Paid, AttemptNo = 3,
                    IdempotencyKey = "advanced-report-payment-retry",
                    IdempotencyPayloadHash = new string('L', 64),
                    CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
                });
            db.PayrollPaymentAllocations.AddRange(
                new PayrollPaymentAllocation
                {
                    Id = 910, BusinessId = 1, PayrollPaymentId = 910, AllocationNo = 1,
                    Method = PayrollPaymentMethods.Cash, Amount = 1000m, SettledAmount = 700m,
                    State = PayrollPaymentAllocationStates.PartiallySettled
                },
                new PayrollPaymentAllocation
                {
                    Id = 911, BusinessId = 1, PayrollPaymentId = 911, AllocationNo = 1,
                    Method = PayrollPaymentMethods.Cash, Amount = 1000m, SettledAmount = 700m,
                    State = PayrollPaymentAllocationStates.PartiallySettled
                },
                new PayrollPaymentAllocation
                {
                    Id = 912, BusinessId = 1, PayrollPaymentId = 912, AllocationNo = 1,
                    Method = PayrollPaymentMethods.Cash, Amount = 1000m, SettledAmount = 1000m,
                    State = PayrollPaymentAllocationStates.Settled
                });
            db.PayrollPaymentReconciliations.AddRange(
                new PayrollPaymentReconciliation
                {
                    Id = 910, BusinessId = 1, PayrollPaymentId = 910, PayrollPaymentAllocationId = 910,
                    Version = 1, PaymentVersion = 1, ExpectedAmount = 1000m, SettledAmount = 700m,
                    Difference = 300m, Status = PayrollPaymentReconciliationStatuses.Exception,
                    SettlementDate = new DateOnly(2026, 10, 15), EvidenceSource = PayrollPaymentMethods.Cash,
                    IdempotencyKey = "advanced-report-reconciliation-1",
                    PayloadHash = new string('M', 64), ReconciledByUserId = 101,
                    ReconciledAtUtc = DateTime.UtcNow
                },
                new PayrollPaymentReconciliation
                {
                    Id = 912, BusinessId = 1, PayrollPaymentId = 912, PayrollPaymentAllocationId = 912,
                    Version = 1, PaymentVersion = 1, ExpectedAmount = 1000m, SettledAmount = 1000m,
                    Difference = 0m, Status = PayrollPaymentReconciliationStatuses.Reconciled,
                    SettlementDate = new DateOnly(2026, 10, 15), EvidenceSource = PayrollPaymentMethods.Bank,
                    IdempotencyKey = "advanced-report-reconciliation-2",
                    PayloadHash = new string('N', 64), ReconciledByUserId = 101,
                    ReconciledAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        const string path = "/api/payroll/v1/reports/advanced";
        Authorize(client, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"{path}?kind=PayrollRegister&from=2026-10-01&to=2026-10-31")).StatusCode);

        SetPermissions(client, "payroll.report.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"{path}?kind=PayrollRegister&from=2026-10-01&to=2026-10-31")).StatusCode);

        SetPermissions(client, "payroll.report.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"{path}/export/csv?kind=PayrollRegister&from=2026-10-01&to=2026-10-31")).StatusCode);

        SetPermissions(client, "payroll.report.view,payroll.salary.view,payroll.export");
        var range = "from=2026-10-01&to=2026-10-31";
        using var registerResponse = await client.GetAsync($"{path}?kind=PayrollRegister&{range}&runId=901");
        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        Assert.True(registerResponse.StatusCode == HttpStatusCode.OK, registerBody);
        var register = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(registerBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var registerRow = Assert.Single(register!.Items);
        Assert.Equal(1200m, registerRow.Gross);
        Assert.Equal(1000m, registerRow.Net);
        Assert.Equal("EMP-1", registerRow.EmployeeCode);

        var components = await client.GetFromJsonAsync<RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(
            $"{path}?kind=PayrollComponents&{range}&runId=901");
        Assert.Equal(3, components!.TotalCount);
        Assert.Contains(components.Items, row => row.ComponentCode == "BASE" && row.Gross == 1200m);
        Assert.Contains(components.Items, row => row.Detail == "LoanRecovery:990:1:1"
            && row.Deductions == 75m);

        var lineage = await client.GetFromJsonAsync<RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(
            $"{path}?kind=CorrectionOffCycle&{range}");
        var lineageRow = Assert.Single(lineage!.Items);
        Assert.Equal(902, lineageRow.RunId);
        Assert.Equal(300m, lineageRow.Gross);
        Assert.Equal(250m, lineageRow.Net);

        using var varianceResponse = await client.GetAsync(
            $"{path}?kind=PayrollVariance&{range}&runId=901&compareRunId=902");
        var varianceBody = await varianceResponse.Content.ReadAsStringAsync();
        Assert.True(varianceResponse.StatusCode == HttpStatusCode.OK, varianceBody);
        var variance = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(varianceBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var varianceRow = Assert.Single(variance!.Items);
        Assert.Equal(-900m, varianceRow.Gross);
        Assert.Equal(-750m, varianceRow.Net);

        using var salaryHistoryResponse = await client.GetAsync(
            $"{path}?kind=SalaryHistory&from=2026-01-01&to=2026-12-31&employeeId=1");
        var salaryHistoryBody = await salaryHistoryResponse.Content.ReadAsStringAsync();
        Assert.True(salaryHistoryResponse.StatusCode == HttpStatusCode.OK, salaryHistoryBody);
        var salaryHistory = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(salaryHistoryBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotEmpty(salaryHistory!.Items);
        Assert.All(salaryHistory.Items, row => Assert.Equal("Approved", row.Status));

        using var runStatusResponse = await client.GetAsync($"{path}?kind=RunStatus&{range}&runId=901");
        var runStatusBody = await runStatusResponse.Content.ReadAsStringAsync();
        Assert.True(runStatusResponse.StatusCode == HttpStatusCode.OK, runStatusBody);
        var runStatus = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(runStatusBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(PayrollRunStatuses.Completed, Assert.Single(runStatus!.Items).Status);

        var loanRecovery = await client.GetFromJsonAsync<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(
            $"{path}?kind=LoanRecovery&{range}&employeeId=1");
        var loanRecoveryRow = Assert.Single(loanRecovery!.Items);
        Assert.Equal(200m, loanRecoveryRow.Opening);
        Assert.Equal(100m, loanRecoveryRow.Recovered);
        Assert.Equal(175m, loanRecoveryRow.Outstanding);

        using var paymentOutstandingResponse = await client.GetAsync(
            $"{path}?kind=PaymentOutstanding&{range}&runId=901");
        var paymentOutstandingBody = await paymentOutstandingResponse.Content.ReadAsStringAsync();
        Assert.True(paymentOutstandingResponse.StatusCode == HttpStatusCode.OK, paymentOutstandingBody);
        var paymentOutstanding = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(paymentOutstandingBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(2, paymentOutstanding!.TotalCount);
        Assert.Contains(paymentOutstanding.Items, row =>
            row.Status == PayrollPaymentStatuses.PartiallyPaid
            && row.Net == 300m
            && row.Detail != null
            && row.Detail.Contains(PayrollPaymentReconciliationStatuses.Exception, StringComparison.Ordinal));
        Assert.Contains(paymentOutstanding.Items, row =>
            row.Status == PayrollPaymentStatuses.Paid
            && row.Net == 0m
            && row.Detail != null
            && row.Detail.Contains(PayrollPaymentReconciliationStatuses.Reconciled, StringComparison.Ordinal));
        Assert.DoesNotContain(paymentOutstanding.Items, row => row.Status == PayrollPaymentStatuses.Returned);

        using var costByLocationResponse = await client.GetAsync(
            $"{path}?kind=CostByLocation&{range}");
        var costByLocationBody = await costByLocationResponse.Content.ReadAsStringAsync();
        Assert.True(costByLocationResponse.StatusCode == HttpStatusCode.OK, costByLocationBody);
        var costByLocation = System.Text.Json.JsonSerializer.Deserialize<
            RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(costByLocationBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(2, costByLocation!.TotalCount);
        Assert.Contains(costByLocation.Items, row =>
            row.LocationId == 10
            && row.Gross == 1500m
            && row.Deductions == 250m
            && row.Net == 1250m
            && row.Detail != null
            && row.Detail.Contains("Outlet", StringComparison.Ordinal));
        Assert.Contains(costByLocation.Items, row =>
            row.LocationId == 11
            && row.Gross == 800m
            && row.Deductions == 100m
            && row.Net == 700m
            && row.Detail != null
            && row.Detail.Contains("Warehouse", StringComparison.Ordinal));

        using var export = await client.GetAsync(
            $"{path}/export/csv?kind=PayrollRegister&{range}&runId=901");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.StartsWith("text/csv", export.Content.Headers.ContentType!.ToString());
        Assert.Contains("PayrollRegister", await export.Content.ReadAsStringAsync());

        using (var auditScope = factory.Services.CreateScope())
        {
            var db = auditScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var viewAudit = await db.AuditEvents.SingleAsync(row =>
                row.Module == "Reports"
                && row.ActionSummary == "Payroll PayrollRegister report viewed.");
            Assert.Contains("PayrollRegister", viewAudit.Metadata);
            Assert.DoesNotContain("EMP-1", viewAudit.Metadata);
            Assert.DoesNotContain("1200", viewAudit.Metadata);
            Assert.DoesNotContain("1000", viewAudit.Metadata);
            Assert.Contains(await db.AuditEvents.Select(row => row.ActionSummary).ToArrayAsync(),
                summary => summary == "Payroll PayrollRegister report exported.");
        }

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        using (var crossBusinessResponse = await client.GetAsync(
            $"{path}?kind=PayrollRegister&{range}&runId=901"))
        {
            var body = await crossBusinessResponse.Content.ReadAsStringAsync();
            if (crossBusinessResponse.StatusCode == HttpStatusCode.OK)
            {
                var isolated = System.Text.Json.JsonSerializer.Deserialize<
                    RetailPOS.Application.HR.Payroll.PayrollAdvancedReportPage>(body,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                Assert.Empty(isolated!.Items);
            }
            else
            {
                Assert.DoesNotContain("EMP-1", body);
                Assert.DoesNotContain("1000", body);
            }
        }

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        SetRole(client, "HR");
        SetIdentity(client, 101, "payroll.report.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"{path}?kind=PayrollRegister&{range}&runId=901&locationId=10")).StatusCode);
    }

    [Fact]
    public async Task AdvancedPayrollReportsRequireEnabledPayrollCapability()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var feature = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
            feature.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.report.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            "/api/payroll/v1/reports/advanced?kind=PayrollRegister&from=2026-10-01&to=2026-10-31"))
            .StatusCode);
    }

    [Fact]
    public async Task AdvancedPayrollReportAuditFailureFailsClosed()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        var before = 0;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            before = await db.AuditEvents.CountAsync();
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.report.view,payroll.salary.view");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "View";
        try
        {
            using var response = await client.GetAsync(
                "/api/payroll/v1/reports/advanced?kind=PayrollRegister&from=2026-10-01&to=2026-10-31");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(before, await verifyDb.AuditEvents.CountAsync());
    }
}
