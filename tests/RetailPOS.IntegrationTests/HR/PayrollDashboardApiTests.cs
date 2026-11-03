using System.Net;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public PayrollRunApiTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    private const string DashboardRoute = "/api/payroll/v1/dashboard";

    [Fact]
    public async Task DashboardRequiresBothPermissionsAndPayrollCapability()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Contains(DashboardRoute, swagger.Paths.Keys);
        await SeedAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.PayrollPeriods.Add(new PayrollPeriod
            {
                Id = 2,
                BusinessId = 2,
                PeriodCode = "2026-10",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow,
                OpenedByUserId = 101
            });
            await db.SaveChangesAsync();
        }
        Authorize(client, "payroll.dashboard.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(DashboardRoute)).StatusCode);
        SetPermissions(client, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(DashboardRoute)).StatusCode);

        SetPermissions(client, "payroll.dashboard.view,payroll.salary.view");
        using var response = await client.GetAsync($"{DashboardRoute}?periodId=1");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using (var json = JsonDocument.Parse(body))
        {
            var emptyRun = json.RootElement.GetProperty("run");
            Assert.Equal(JsonValueKind.Null, emptyRun.GetProperty("id").ValueKind);
            Assert.Equal(0m, emptyRun.GetProperty("gross").GetDecimal());
            Assert.Equal(0, emptyRun.GetProperty("employeeCount").GetInt64());
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{DashboardRoute}?periodId=2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{DashboardRoute}?locationId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync(
                $"{DashboardRoute}?fromDate=2026-10-31&toDate=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{DashboardRoute}?runId=999")).StatusCode);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        using var disabledClient = disabledFactory.CreateClient();
        await SeedAsync(disabledFactory.Services, payrollEnabled: false);
        Authorize(disabledClient, "payroll.dashboard.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabledClient.GetAsync(DashboardRoute)).StatusCode);
    }

    [Fact]
    public async Task DashboardAggregatesPersistedPayrollAndMixedPaymentEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        var measurements = new DashboardQueryMeasurementInterceptor();
        factory.AdditionalDbCommandInterceptor = measurements;
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var salaryVersionId = await db.PayrollEmployeeSalaryVersions
                .Where(row => row.BusinessId == 1 && row.EmployeeId == 1)
                .Select(row => row.Id)
                .SingleAsync();
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = 1,
                BusinessId = 1,
                PayrollPeriodId = 1,
                RunNo = 1,
                RunType = PayrollRunTypes.Regular,
                ScopeType = PayrollRunScopeTypes.Business,
                ScopeKey = PayrollRunScopeTypes.BusinessKey,
                Status = PayrollRunStatuses.Completed,
                CurrencyCode = "BDT",
                DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CalculationVersion = 1,
                IdempotencyKey = "dashboard-run",
                IdempotencyPayloadHash = new string('A', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = 2,
                BusinessId = 1,
                PayrollPeriodId = 1,
                RunNo = 2,
                RunType = PayrollRunTypes.Correction,
                ScopeType = PayrollRunScopeTypes.Location,
                ScopeId = 20,
                ScopeKey = PayrollRunScopeTypes.LocationKey(20),
                Status = PayrollRunStatuses.Completed,
                CurrencyCode = "BDT",
                DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CalculationVersion = 1,
                IdempotencyKey = "dashboard-location-run",
                IdempotencyPayloadHash = new string('D', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();

            db.PayrollEmployeeResults.Add(new PayrollEmployeeResult
            {
                Id = 1,
                BusinessId = 1,
                PayrollRunId = 1,
                CalculationVersion = 1,
                CalculationInputHash = new string('B', 64),
                EmployeeId = 1,
                EmployeeCodeSnapshot = "EMP-1",
                EmployeeNameSnapshot = "Dashboard Employee",
                LocationIdSnapshot = 10,
                SalaryVersionId = salaryVersionId,
                SalaryEffectiveFromSnapshot = new DateOnly(2026, 1, 1),
                GrossEarnings = 1_200m,
                TotalDeductions = 200m,
                NetPay = 1_000m,
                Status = PayrollEmployeeResultStatuses.Calculated,
                CalculatedAtUtc = DateTime.UtcNow
            });
            var bankDestination = new PayrollEmployeePaymentDestination
            {
                BusinessId = 1,
                EmployeeId = 1,
                VersionNo = 2,
                Version = 1,
                OperationalLocationProjectionId = 10,
                Method = PayrollPaymentDestinationMethods.Bank,
                IsPrimary = true,
                IsActive = true,
                EffectiveFrom = new DateOnly(2026, 2, 1),
                BankName = "Dashboard Bank",
                AccountName = "Dashboard Account",
                AccountNumber = "123456789",
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.PayrollEmployeePaymentDestinations.Add(bankDestination);
            await db.SaveChangesAsync();
            db.PayrollPayments.Add(new PayrollPayment
            {
                Id = 1,
                BusinessId = 1,
                PayrollPeriodId = 1,
                PayrollRunId = 1,
                PayrollEmployeeResultId = 1,
                EmployeeId = 1,
                PayableAmount = 1_000m,
                PaidAmount = 700m,
                OutstandingAmount = 300m,
                CurrencyCode = "BDT",
                Method = PayrollPaymentMethods.Mixed,
                Status = PayrollPaymentStatuses.PartiallyPaid,
                IdempotencyKey = "dashboard-payment",
                IdempotencyPayloadHash = new string('C', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow,
                Allocations =
                [
                    new PayrollPaymentAllocation
                    {
                        BusinessId = 1, AllocationNo = 1, Method = PayrollPaymentMethods.Cash,
                        Amount = 400m, SettledAmount = 400m,
                        State = PayrollPaymentAllocationStates.Settled
                    },
                    new PayrollPaymentAllocation
                    {
                        BusinessId = 1, AllocationNo = 2, Method = PayrollPaymentMethods.Bank,
                        Amount = 600m, SettledAmount = 300m,
                        State = PayrollPaymentAllocationStates.PartiallySettled,
                        PaymentDestinationId = bankDestination.Id,
                        PaymentDestinationVersionNo = bankDestination.VersionNo,
                        BankNameSnapshot = bankDestination.BankName,
                        AccountNameSnapshot = bankDestination.AccountName,
                        AccountNumberSnapshot = bankDestination.AccountNumber
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.dashboard.view,payroll.salary.view");
        measurements.Reset();
        var requestTimer = Stopwatch.StartNew();
        using var response = await client.GetAsync($"{DashboardRoute}?periodId=1");
        requestTimer.Stop();
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var snapshot = measurements.Snapshot();
        Assert.InRange(snapshot.CommandCount, 1, 50);
        Assert.NotNull(snapshot.SlowestDataCommand);
        output.WriteLine(
            $"Payroll dashboard measurement: provider=SQLite; SQL commands={snapshot.CommandCount}; "
            + $"cumulative SQL={snapshot.Cumulative.TotalMilliseconds:F2} ms; "
            + $"slowest meaningful query={snapshot.SlowestDataQuery.TotalMilliseconds:F2} ms; "
            + $"HTTP={requestTimer.Elapsed.TotalMilliseconds:F2} ms.");
        output.WriteLine($"Slowest Payroll dashboard SQL: {snapshot.SlowestDataCommand}");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(1_200m, root.GetProperty("run").GetProperty("gross").GetDecimal());
        Assert.Equal(200m, root.GetProperty("run").GetProperty("totalDeductions").GetDecimal());
        Assert.Equal(1_000m, root.GetProperty("run").GetProperty("netPayable").GetDecimal());
        Assert.Equal(700m, root.GetProperty("payments").GetProperty("totalPaid").GetDecimal());
        Assert.Equal(300m, root.GetProperty("payments").GetProperty("totalOutstanding").GetDecimal());
        Assert.Equal(1, root.GetProperty("blockers").GetProperty("partialPaymentCount").GetInt64());
        Assert.Equal(0m, root.GetProperty("finalSettlements")
            .GetProperty("approvedPositiveAmount").GetDecimal());
        Assert.False(root.GetProperty("accounting").GetProperty("enabled").GetBoolean());
        Assert.DoesNotContain("accountNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bankName", body, StringComparison.OrdinalIgnoreCase);

        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        using var scopedClient = factory.CreateClient();
        Authorize(scopedClient, "payroll.dashboard.view,payroll.salary.view");
        SetRole(scopedClient, "HR");
        var scopedResponse = await scopedClient.GetAsync(
            $"{DashboardRoute}?periodId=1&runType={PayrollRunTypes.Correction}");
        var scopedBody = await scopedResponse.Content.ReadAsStringAsync();
        Assert.True(scopedResponse.StatusCode == HttpStatusCode.OK, scopedBody);
        using var scopedJson = JsonDocument.Parse(scopedBody);
        Assert.Equal(JsonValueKind.Null,
            scopedJson.RootElement.GetProperty("run").GetProperty("id").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await scopedClient.GetAsync($"{DashboardRoute}?periodId=1&locationId=20")).StatusCode);
    }

    [Fact]
    public async Task DashboardAggregatesPersistedAccountingRunLineageRetryAndReconciliationEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);

        long regularRunId;
        long correctionRunId;
        long offCycleRunId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var salaryVersionId = await db.PayrollEmployeeSalaryVersions
                .Where(row => row.BusinessId == 1 && row.EmployeeId == 1)
                .Select(row => row.Id)
                .SingleAsync();
            var now = DateTime.UtcNow;
            var regular = CreateDashboardRun(
                PayrollRunTypes.Regular, now.AddMinutes(2), PayrollRunScopeTypes.Business);
            db.PayrollRuns.Add(regular);
            await db.SaveChangesAsync();
            regularRunId = regular.Id;

            var correction = CreateDashboardRun(
                PayrollRunTypes.Correction, now.AddMinutes(1), PayrollRunScopeTypes.Business);
            correction.OriginalPayrollRunId = regular.Id;
            correction.OriginalCalculationVersion = 1;
            correction.OriginalEvidenceHash = new string('R', 64);
            correction.CorrectionReference = "DASHBOARD-CORRECTION";
            correction.CorrectionType = "Replacement";
            correction.CorrectionReason = "Acceptance fixture";
            correction.PaymentCorrectionRequired = true;
            correction.DownstreamReversalRequired = true;
            var offCycle = CreateDashboardRun(
                PayrollRunTypes.OffCycle, now, PayrollRunScopeTypes.Business);
            var auxiliaryLocations = new[] { 31L, 32L, 33L, 34L, 35L };
            foreach (var locationId in auxiliaryLocations)
            {
                db.Outlets.Add(new Outlet
                {
                    Id = locationId,
                    BusinessId = 1,
                    Name = $"Accounting fixture outlet {locationId}"
                });
                db.OperationalLocationProjections.Add(new OperationalLocationProjection
                {
                    Id = locationId,
                    BusinessId = 1,
                    SourceSystem = "RetailPOS",
                    SourceEntityType = "Outlet",
                    SourceEntityId = locationId.ToString(),
                    Name = $"Accounting fixture outlet {locationId}",
                    SourceVersion = 1
                });
            }
            var accountingRuns = new List<PayrollRun>();
            foreach (var locationId in auxiliaryLocations)
            {
                var auxiliaryRun = CreateDashboardRun(
                    PayrollRunTypes.Regular, now.AddMinutes(-locationId), PayrollRunScopeTypes.Location);
                auxiliaryRun.ScopeId = locationId;
                auxiliaryRun.ScopeKey = PayrollRunScopeTypes.LocationKey(locationId);
                accountingRuns.Add(auxiliaryRun);
            }
            db.PayrollRuns.AddRange(correction, offCycle);

            var otherLocation = new OperationalLocationProjection
            {
                Id = 30,
                BusinessId = 1,
                SourceSystem = "RetailPOS",
                SourceEntityType = "Outlet",
                SourceEntityId = "30",
                Name = "Out of scope outlet",
                SourceVersion = 1
            };
            db.Outlets.Add(new Outlet { Id = 30, BusinessId = 1, Name = "Out of scope outlet" });
            db.OperationalLocationProjections.Add(otherLocation);
            var outOfScopeRun = CreateDashboardRun(
                PayrollRunTypes.Regular, now.AddMinutes(-10), PayrollRunScopeTypes.Location);
            outOfScopeRun.ScopeId = 30;
            outOfScopeRun.ScopeKey = PayrollRunScopeTypes.LocationKey(30);
            db.PayrollRuns.AddRange(accountingRuns);
            db.PayrollRuns.Add(outOfScopeRun);

            var otherBusinessPeriod = new PayrollPeriod
            {
                Id = 2,
                BusinessId = 2,
                PeriodCode = "2026-10",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31),
                CreatedAtUtc = now,
                CreatedByUserId = 101,
                OpenedAtUtc = now,
                OpenedByUserId = 101
            };
            db.PayrollPeriods.Add(otherBusinessPeriod);
            var otherBusinessRun = CreateDashboardRun(
                PayrollRunTypes.Regular, now, PayrollRunScopeTypes.Business, businessId: 2, periodId: 2);
            db.PayrollRuns.Add(otherBusinessRun);
            await db.SaveChangesAsync();

            correctionRunId = correction.Id;
            offCycleRunId = offCycle.Id;

            var resultRows = new List<(PayrollRun Run, decimal Gross, decimal Deductions, decimal Net, long LocationId)>();
            resultRows.Add((regular, 1_200m, 200m, 1_000m, 10));
            resultRows.Add((correction, 1_320m, 220m, 1_100m, 10));
            resultRows.Add((offCycle, 300m, 50m, 250m, 10));
            foreach (var run in accountingRuns)
                resultRows.Add((run, 1m, 0m, 1m, run.ScopeId!.Value));
            resultRows.Add((outOfScopeRun, 9_999m, 0m, 9_999m, 30));

            var results = resultRows.Select((item, index) => new PayrollEmployeeResult
            {
                BusinessId = item.Run.BusinessId,
                PayrollRunId = item.Run.Id,
                CalculationVersion = 1,
                CalculationInputHash = new string((char)('A' + index), 64),
                EmployeeId = 1,
                EmployeeCodeSnapshot = $"DASH-{index}",
                EmployeeNameSnapshot = "Dashboard Employee",
                LocationIdSnapshot = item.LocationId,
                SalaryVersionId = salaryVersionId,
                SalaryEffectiveFromSnapshot = new DateOnly(2026, 1, 1),
                GrossEarnings = item.Gross,
                TotalDeductions = item.Deductions,
                NetPay = item.Net,
                Status = PayrollEmployeeResultStatuses.Calculated,
                CalculatedAtUtc = now
            }).ToArray();
            db.PayrollEmployeeResults.AddRange(results);

            var foreignRun = otherBusinessRun;
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1,
                FeatureKey = "accounting.enabled",
                IsEnabled = true
            });
            await db.SaveChangesAsync();

            var statusOrder = new[]
            {
                PayrollAccountingPostingStatuses.Pending,
                PayrollAccountingPostingStatuses.Processing,
                PayrollAccountingPostingStatuses.Conflict,
                PayrollAccountingPostingStatuses.Posted,
                PayrollAccountingPostingStatuses.Failed
            };
            var accountingEvidence = new List<(PayrollRun Run, PayrollEmployeeResult Result, string Status)>();
            for (var index = 0; index < statusOrder.Length; index++)
                accountingEvidence.Add((accountingRuns[index], results.Single(row =>
                    row.PayrollRunId == accountingRuns[index].Id), statusOrder[index]));

            var accountingPayments = accountingEvidence.Select((evidence, index) =>
                CreateAccountingEvidencePayment(evidence.Run, evidence.Result, index + 1, now)).ToArray();
            db.PayrollPayments.AddRange(accountingPayments);
            await db.SaveChangesAsync();

            var allocations = accountingPayments.Select((payment, index) =>
                CreateDashboardAllocation(payment, index + 1, 1m, 1m)).ToArray();
            db.PayrollPaymentAllocations.AddRange(allocations);
            await db.SaveChangesAsync();
            var paymentEvidence = accountingPayments.Select((payment, index) =>
                CreateDashboardPaymentEvidence(payment, index + 1, now)).ToArray();
            db.PayrollPaymentEvidence.AddRange(paymentEvidence);
            await db.SaveChangesAsync();
            var accountingReconciliations = allocations.Select((allocation, index) =>
                CreateDashboardReconciliation(
                    accountingPayments[index], allocation, 1, 1m, 1m,
                    PayrollPaymentReconciliationStatuses.Reconciled, index + 1, now)).ToArray();
            db.PayrollPaymentReconciliations.AddRange(accountingReconciliations);
            await db.SaveChangesAsync();

            var expensePostings = accountingEvidence.Select((evidence, index) =>
                CreateDashboardExpensePosting(evidence.Run, index + 1, evidence.Status, now)).ToArray();
            var salaryPostings = accountingEvidence.Select((evidence, index) =>
                CreateDashboardSalaryPosting(
                    evidence.Run,
                    accountingPayments[index],
                    allocations[index],
                    paymentEvidence[index],
                    evidence.Status == PayrollAccountingPostingStatuses.Posted
                        ? accountingReconciliations[index].Id
                        : null,
                    index + 1,
                    evidence.Status,
                    now)).ToArray();
            db.PayrollAccountingPostings.AddRange(expensePostings);
            db.PayrollAccountingPostings.AddRange(salaryPostings);
            db.PayrollAccountingPostings.Add(CreateDashboardExpensePosting(
                outOfScopeRun, 99, PayrollAccountingPostingStatuses.Conflict, now));
            db.PayrollAccountingPostings.Add(CreateDashboardExpensePosting(
                otherBusinessRun, 100, PayrollAccountingPostingStatuses.Conflict, now));
            await db.SaveChangesAsync();

            var returnedPayment = CreateDashboardPayment(
                regular, results.Single(row => row.PayrollRunId == regular.Id), 20,
                1_000m, 700m, 300m, PayrollPaymentStatuses.Returned, now);
            returnedPayment.ResolutionStatus = PayrollPaymentResolutionStatuses.ResolvedReissue;
            db.PayrollPayments.Add(returnedPayment);
            await db.SaveChangesAsync();
            var returnedAllocation = CreateDashboardAllocation(returnedPayment, 20, 1_000m, 700m);
            db.PayrollPaymentAllocations.Add(returnedAllocation);
            await db.SaveChangesAsync();
            db.PayrollPaymentEvidence.AddRange(
                CreateDashboardPaymentEvidence(
                    returnedPayment, 20, now, PayrollPaymentEvidenceTypes.Settlement, 700m),
                CreateDashboardPaymentEvidence(
                    returnedPayment, 21, now, PayrollPaymentEvidenceTypes.Return, 300m));
            await db.SaveChangesAsync();

            var retry = CreateDashboardPayment(
                regular, results.Single(row => row.PayrollRunId == regular.Id), 21,
                300m, 300m, 0m, PayrollPaymentStatuses.Paid, now);
            retry.OriginalPaymentId = returnedPayment.Id;
            retry.AttemptNo = 2;
            db.PayrollPayments.Add(retry);
            await db.SaveChangesAsync();
            var retryAllocations = Enumerable.Range(0, 4)
                .Select(index => CreateDashboardAllocation(retry, 30 + index, 75m, 75m))
                .ToArray();
            db.PayrollPaymentAllocations.AddRange(retryAllocations);
            await db.SaveChangesAsync();
            db.PayrollPaymentEvidence.Add(CreateDashboardPaymentEvidence(retry, 30, now, amount: 300m));
            await db.SaveChangesAsync();
            var reconciliationStatuses = new[]
            {
                PayrollPaymentReconciliationStatuses.Reconciled,
                PayrollPaymentReconciliationStatuses.PartiallyReconciled,
                PayrollPaymentReconciliationStatuses.Unreconciled,
                PayrollPaymentReconciliationStatuses.Exception
            };
            var reconciliationVersions = new List<PayrollPaymentReconciliation>();
            for (var index = 0; index < retryAllocations.Length; index++)
            {
                var settled = reconciliationStatuses[index] switch
                {
                    PayrollPaymentReconciliationStatuses.Reconciled => 75m,
                    PayrollPaymentReconciliationStatuses.PartiallyReconciled => 35m,
                    PayrollPaymentReconciliationStatuses.Exception => 10m,
                    _ => 0m
                };
                reconciliationVersions.Add(CreateDashboardReconciliation(
                    retry, retryAllocations[index], 1, 75m, settled,
                    index == 0
                        ? PayrollPaymentReconciliationStatuses.Unreconciled
                        : reconciliationStatuses[index],
                    30 + index, now));
                if (index == 0)
                    reconciliationVersions.Add(CreateDashboardReconciliation(
                        retry, retryAllocations[index], 2, 75m, 75m,
                        PayrollPaymentReconciliationStatuses.Reconciled, 40, now));
            }
            db.PayrollPaymentReconciliations.AddRange(reconciliationVersions);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.dashboard.view,payroll.salary.view");
        using var periodResponse = await client.GetAsync($"{DashboardRoute}?periodId=1");
        var periodBody = await periodResponse.Content.ReadAsStringAsync();
        Assert.True(periodResponse.StatusCode == HttpStatusCode.OK, periodBody);
        using var periodJson = JsonDocument.Parse(periodBody);
        var root = periodJson.RootElement;
        Assert.Equal(regularRunId, root.GetProperty("run").GetProperty("id").GetInt64());
        Assert.Equal(1_200m, root.GetProperty("run").GetProperty("gross").GetDecimal());
        Assert.Equal(1_000m, root.GetProperty("run").GetProperty("netPayable").GetDecimal());
        Assert.Equal(1_000m, root.GetProperty("payments").GetProperty("totalPayable").GetDecimal());
        Assert.Equal(1_000m, root.GetProperty("payments").GetProperty("totalPaid").GetDecimal());
        Assert.Equal(0m, root.GetProperty("payments").GetProperty("totalOutstanding").GetDecimal());
        Assert.Equal(1, root.GetProperty("payments").GetProperty("returnedCount").GetInt64());
        using (var verificationScope = factory.Services.CreateScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var retry = await db.PayrollPayments.SingleAsync(row =>
                row.BusinessId == 1 && row.PayrollRunId == regularRunId
                && row.OriginalPaymentId.HasValue);
            var returned = await db.PayrollPayments.SingleAsync(row => row.Id == retry.OriginalPaymentId);
            Assert.Equal(700m, returned.PaidAmount);
            Assert.Equal(300m, returned.OutstandingAmount);
            Assert.Equal(300m, retry.PaidAmount);
            Assert.Equal(0m, retry.OutstandingAmount);
            Assert.Contains(await db.PayrollPaymentEvidence
                .Where(row => row.PayrollPaymentId == returned.Id)
                .Select(row => row.Type)
                .ToArrayAsync(), value => value == PayrollPaymentEvidenceTypes.Return);
        }

        var accounting = root.GetProperty("accounting");
        Assert.True(accounting.GetProperty("enabled").GetBoolean());
        foreach (var status in new[]
        {
            PayrollAccountingPostingStatuses.Pending,
            PayrollAccountingPostingStatuses.Processing,
            PayrollAccountingPostingStatuses.Posted,
            PayrollAccountingPostingStatuses.Failed,
            PayrollAccountingPostingStatuses.Conflict
        })
        {
            Assert.Equal(
                status == PayrollAccountingPostingStatuses.Conflict ? 2 : 1,
                DashboardPostingCount(
                    accounting.GetProperty("expensePostings"), PayrollAccountingPostingTypes.PayrollExpense, status));
            Assert.Equal(1, DashboardPostingCount(
                accounting.GetProperty("settlementPostings"), PayrollAccountingPostingTypes.SalaryPayment, status));
        }
        Assert.Equal(9, accounting.GetProperty("blockerCount").GetInt64());

        var reconciliation = root.GetProperty("reconciliation");
        Assert.Equal(1, reconciliation.GetProperty("reconciledCount").GetInt64());
        Assert.Equal(1, reconciliation.GetProperty("partiallyReconciledCount").GetInt64());
        Assert.Equal(1, reconciliation.GetProperty("unreconciledCount").GetInt64());
        Assert.Equal(1, reconciliation.GetProperty("exceptionCount").GetInt64());

        foreach (var (runId, expectedType, gross, net) in new[]
        {
            (correctionRunId, PayrollRunTypes.Correction, 1_320m, 1_100m),
            (offCycleRunId, PayrollRunTypes.OffCycle, 300m, 250m)
        })
        {
            using var response = await client.GetAsync($"{DashboardRoute}?runId={runId}");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            var run = json.RootElement.GetProperty("run");
            Assert.Equal(expectedType, run.GetProperty("runType").GetString());
            Assert.Equal(gross, run.GetProperty("gross").GetDecimal());
            Assert.Equal(net, run.GetProperty("netPayable").GetDecimal());
        }

        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        using var scopedClient = factory.CreateClient();
        Authorize(scopedClient, "payroll.dashboard.view,payroll.salary.view");
        SetRole(scopedClient, "HR");
        using var scopedResponse = await scopedClient.GetAsync($"{DashboardRoute}?periodId=1");
        var scopedBody = await scopedResponse.Content.ReadAsStringAsync();
        Assert.True(scopedResponse.StatusCode == HttpStatusCode.OK, scopedBody);
        using var scopedJson = JsonDocument.Parse(scopedBody);
        Assert.Equal(0, DashboardPostingCount(
            scopedJson.RootElement.GetProperty("accounting").GetProperty("expensePostings"),
            PayrollAccountingPostingTypes.PayrollExpense,
            PayrollAccountingPostingStatuses.Conflict));
        Assert.Equal(1_200m, scopedJson.RootElement.GetProperty("run").GetProperty("gross").GetDecimal());
    }

    [Fact]
    public async Task DashboardAggregatesOnlyApprovedPersistedPositiveAndNegativeFinalSettlements()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "accounting.enabled", IsEnabled = true
            });
            db.Outlets.Add(new Outlet { Id = 30, BusinessId = 1, Name = "Other outlet" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                Id = 30, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                SourceEntityId = "30", Name = "Other outlet", SourceVersion = 1
            });
            db.HrEmploymentTypes.Add(new HrEmploymentType
            {
                Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent"
            });
            db.HrEmployees.Add(new HrEmployee
            {
                Id = 2, BusinessId = 2, EmployeeCode = "FOREIGN",
                FirstName = "Foreign", DisplayName = "Foreign Employee",
                CurrentEmploymentTypeId = 2, JoiningDate = new DateOnly(2026, 1, 1)
            });
            db.Users.Add(new User
            {
                Id = 202, BusinessId = 2, Name = "Foreign Actor",
                Email = "foreign-dashboard@example.invalid", PasswordHash = "test",
                IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAll
            });
            db.HrClearanceTemplates.AddRange(
                CreateDashboardClearanceTemplate(1, "Dashboard", 10),
                CreateDashboardClearanceTemplate(2, "Foreign", 20));
            db.ApprovalPolicies.AddRange(
                CreateDashboardFinalSettlementPolicy(1, "DASHBOARD-FINAL"),
                CreateDashboardFinalSettlementPolicy(2, "FOREIGN-FINAL"));
            await db.SaveChangesAsync();

            var template1 = await db.HrClearanceTemplates.SingleAsync(row => row.BusinessId == 1);
            var template2 = await db.HrClearanceTemplates.SingleAsync(row => row.BusinessId == 2);
            var policy1 = await db.ApprovalPolicies.SingleAsync(row => row.BusinessId == 1);
            var policy2 = await db.ApprovalPolicies.SingleAsync(row => row.BusinessId == 2);
            var exits = new[]
            {
                CreateDashboardExitCase(1, 1, template1.Id, 10, 101, "POS"),
                CreateDashboardExitCase(1, 1, template1.Id, 10, 101, "POS"),
                CreateDashboardExitCase(1, 1, template1.Id, 10, 101, "POS"),
                CreateDashboardExitCase(1, 1, template1.Id, 30, 101, "OUT"),
                CreateDashboardExitCase(2, 2, template2.Id, 20, 202, "FOREIGN")
            };
            db.HrExitCases.AddRange(exits);
            await db.SaveChangesAsync();

            await AddApprovedDashboardSettlementAsync(
                db, exits[0], policy1.Id, 1_000m, 700m, "positive-partial");
            await AddApprovedDashboardSettlementAsync(
                db, exits[1], policy1.Id, 500m, 500m, "positive-paid");
            var negative = await AddApprovedDashboardSettlementAsync(
                db, exits[2], policy1.Id, -300m, 0m, "negative-receivable");
            await AddDashboardSettlementPostingAsync(
                db, negative.Settlement, negative.Revision, PayrollFinalSettlementPostingTypes.Receivable);
            await AddApprovedDashboardSettlementAsync(
                db, exits[3], policy1.Id, 50_000m, 50m, "location-excluded");
            await AddApprovedDashboardSettlementAsync(
                db, exits[4], policy2.Id, 90_000m, 100m, "business-excluded");
            var outOfScopeSettlement = await db.PayrollFinalSettlements
                .SingleAsync(row => row.ExitCaseId == exits[3].Id);
            var trackedOutOfScopePayment = await db.PayrollPayments
                .SingleAsync(row => row.FinalSettlementId == outOfScopeSettlement.Id);
            Assert.Equal(50_000m, trackedOutOfScopePayment.PayableAmount);
            var foreignSettlement = await db.PayrollFinalSettlements
                .SingleAsync(row => row.ExitCaseId == exits[4].Id);
            Assert.True(await db.PayrollPayments.AnyAsync(row =>
                row.BusinessId == 2 && row.FinalSettlementId == foreignSettlement.Id));
        }

        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        using var client = factory.CreateClient();
        Authorize(client, "payroll.dashboard.view,payroll.salary.view");
        SetRole(client, "HR");
        using var response = await client.GetAsync($"{DashboardRoute}?periodId=1");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var summary = json.RootElement.GetProperty("finalSettlements");
        Assert.Equal(2, summary.GetProperty("approvedPositiveCount").GetInt64());
        Assert.Equal(1_500m, summary.GetProperty("approvedPositiveAmount").GetDecimal());
        Assert.Equal(1_200m, summary.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(300m, summary.GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(1, summary.GetProperty("negativeReceivableCount").GetInt64());
        Assert.Equal(300m, summary.GetProperty("negativeReceivableAmount").GetDecimal());
        Assert.Equal(2, summary.GetProperty("financiallyCompletedCount").GetInt64());
        Assert.Equal(1, summary.GetProperty("unresolvedFinancialCount").GetInt64());
        Assert.Equal(0m, json.RootElement.GetProperty("payments").GetProperty("totalPaid").GetDecimal());
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var negativeExitId = await verifyDb.HrExitCases
            .Where(exit => exit.BusinessId == 1 && exit.Reason == "Dashboard acceptance fixture")
            .OrderBy(exit => exit.Id)
            .Skip(2)
            .Select(exit => exit.Id)
            .FirstAsync();
        var negativeSettlement = await verifyDb.PayrollFinalSettlements.SingleAsync(row =>
            row.ExitCaseId == negativeExitId);
        Assert.False(await verifyDb.PayrollPayments.AnyAsync(row =>
            row.BusinessId == 1 && row.FinalSettlementId == negativeSettlement.Id));
    }

    private static PayrollRun CreateDashboardRun(
        string runType, DateTime createdAtUtc, string scopeType, long businessId = 1, long periodId = 1) => new()
    {
        BusinessId = businessId,
        PayrollPeriodId = periodId,
        RunNo = (int)(createdAtUtc.Ticks % int.MaxValue),
        RunType = runType,
        ScopeType = scopeType,
        ScopeKey = scopeType == PayrollRunScopeTypes.Business
            ? PayrollRunScopeTypes.BusinessKey
            : "LOCATION:30",
        Status = PayrollRunStatuses.Completed,
        CurrencyCode = "BDT",
        DecimalPrecision = 2,
        RoundingMode = MidpointRounding.AwayFromZero.ToString(),
        CalculationVersion = 1,
        IdempotencyKey = $"dashboard-{Guid.NewGuid():N}",
        IdempotencyPayloadHash = new string('A', 64),
        CreatedByUserId = 101,
        CreatedAtUtc = createdAtUtc
    };

    private static PayrollPayment CreateAccountingEvidencePayment(
        PayrollRun run, PayrollEmployeeResult result, int attemptNo, DateTime createdAtUtc) => new()
    {
        BusinessId = run.BusinessId,
        PayrollPeriodId = run.PayrollPeriodId,
        PayrollRunId = run.Id,
        PayrollEmployeeResultId = result.Id,
        EmployeeId = result.EmployeeId,
        HistoricalLocationIdSnapshot = result.LocationIdSnapshot,
        PayableAmount = 1m,
        PaidAmount = 1m,
        OutstandingAmount = 0m,
        CurrencyCode = "BDT",
        Method = PayrollPaymentMethods.Cash,
        Status = PayrollPaymentStatuses.Paid,
        AttemptNo = attemptNo,
        IdempotencyKey = $"dashboard-payment-{Guid.NewGuid():N}",
        IdempotencyPayloadHash = new string('P', 64),
        CreatedByUserId = 101,
        CreatedAtUtc = createdAtUtc
    };

    private static PayrollPayment CreateDashboardPayment(
        PayrollRun run,
        PayrollEmployeeResult result,
        int attemptNo,
        decimal payable,
        decimal paid,
        decimal outstanding,
        string status,
        DateTime createdAtUtc) => new()
    {
        BusinessId = run.BusinessId,
        PayrollPeriodId = run.PayrollPeriodId,
        PayrollRunId = run.Id,
        PayrollEmployeeResultId = result.Id,
        EmployeeId = result.EmployeeId,
        HistoricalLocationIdSnapshot = result.LocationIdSnapshot,
        PayableAmount = payable,
        PaidAmount = paid,
        OutstandingAmount = outstanding,
        CurrencyCode = "BDT",
        Method = PayrollPaymentMethods.Cash,
        Status = status,
        AttemptNo = attemptNo,
        IdempotencyKey = $"dashboard-payment-{Guid.NewGuid():N}",
        IdempotencyPayloadHash = new string('P', 64),
        CreatedByUserId = 101,
        CreatedAtUtc = createdAtUtc
    };

    private static PayrollPaymentAllocation CreateDashboardAllocation(
        PayrollPayment payment, int allocationNo, decimal amount, decimal settledAmount) => new()
    {
        BusinessId = payment.BusinessId,
        PayrollPaymentId = payment.Id,
        AllocationNo = allocationNo,
        Method = PayrollPaymentMethods.Cash,
        Amount = amount,
        SettledAmount = settledAmount,
        State = settledAmount == amount
            ? PayrollPaymentAllocationStates.Settled
            : settledAmount > 0m
                ? PayrollPaymentAllocationStates.PartiallySettled
                : PayrollPaymentAllocationStates.Pending
    };

    private static PayrollPaymentEvidence CreateDashboardPaymentEvidence(
        PayrollPayment payment,
        int evidenceNo,
        DateTime timestamp,
        string type = PayrollPaymentEvidenceTypes.Settlement,
        decimal? amount = null) => new()
    {
        BusinessId = payment.BusinessId,
        PayrollPaymentId = payment.Id,
        Type = type,
        Amount = amount ?? payment.PaidAmount,
        EvidenceDate = new DateOnly(2026, 10, 15),
        EvidenceReference = $"DASH-EVIDENCE-{evidenceNo}",
        AllocationEvidenceJson = "[]",
        IdempotencyKey = $"dashboard-evidence-{Guid.NewGuid():N}",
        PayloadHash = new string('E', 64),
        VerifiedByUserId = 101,
        VerifiedAtUtc = timestamp
    };

    private static PayrollPaymentReconciliation CreateDashboardReconciliation(
        PayrollPayment payment,
        PayrollPaymentAllocation allocation,
        long version,
        decimal expected,
        decimal settled,
        string status,
        int identity,
        DateTime timestamp) => new()
    {
        BusinessId = payment.BusinessId,
        PayrollPaymentId = payment.Id,
        PayrollPaymentAllocationId = allocation.Id,
        Version = version,
        PaymentVersion = payment.Version,
        ExpectedAmount = expected,
        SettledAmount = settled,
        Difference = expected - settled,
        Status = status,
        SettlementDate = new DateOnly(2026, 10, 15),
        EvidenceSource = PayrollPaymentMethods.Cash,
        IdempotencyKey = $"dashboard-reconciliation-{identity}-{version}",
        PayloadHash = new string('C', 64),
        ReconciledByUserId = 101,
        ReconciledAtUtc = timestamp
    };

    private static PayrollAccountingPosting CreateDashboardExpensePosting(
        PayrollRun run, int identity, string status, DateTime timestamp) => new()
    {
        BusinessId = run.BusinessId,
        PayrollPeriodId = run.PayrollPeriodId,
        PayrollRunId = run.Id,
        RunRevision = 1,
        PostingType = PayrollAccountingPostingTypes.PayrollExpense,
        PostingReference = $"DASH-EXPENSE-{identity}",
        IdempotencyKey = $"dashboard-expense-{identity}",
        Status = status,
        RequestedByUserId = 101,
        RequestedAtUtc = timestamp
    };

    private static PayrollAccountingPosting CreateDashboardSalaryPosting(
        PayrollRun run,
        PayrollPayment payment,
        PayrollPaymentAllocation allocation,
        PayrollPaymentEvidence evidence,
        long? reconciliationId,
        int identity,
        string status,
        DateTime timestamp) => new()
    {
        BusinessId = run.BusinessId,
        PayrollPeriodId = run.PayrollPeriodId,
        PayrollRunId = run.Id,
        RunRevision = 1,
        PostingType = PayrollAccountingPostingTypes.SalaryPayment,
        PayrollPaymentId = payment.Id,
        PayrollPaymentAllocationId = allocation.Id,
        PayrollPaymentEvidenceId = evidence.Id,
        PayrollPaymentReconciliationId = reconciliationId,
        PostingReference = $"DASH-SALARY-{identity}",
        IdempotencyKey = $"dashboard-salary-{identity}",
        Status = status,
        RequestedByUserId = 101,
        RequestedAtUtc = timestamp
    };

    private static long DashboardPostingCount(
        JsonElement metrics, string category, string status) =>
        metrics.EnumerateArray()
            .Where(row => row.GetProperty("category").GetString() == category
                && row.GetProperty("status").GetString() == status)
            .Sum(row => row.GetProperty("count").GetInt64());

    private static HrClearanceTemplate CreateDashboardClearanceTemplate(
        long businessId, string name, long locationId) => new()
    {
        BusinessId = businessId,
        Code = $"DASH-{businessId}-{locationId}",
        Name = name,
        ScopeType = HrClearanceTemplateScopeType.Business,
        VersionNo = 1,
        EffectiveFrom = new DateOnly(2020, 1, 1),
        IsActive = true,
        CreatedByUserId = businessId == 1 ? 101 : 202,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static ApprovalPolicy CreateDashboardFinalSettlementPolicy(long businessId, string code) => new()
    {
        BusinessId = businessId,
        Code = code,
        ProcessType = ApprovalProcessType.FinalSettlement,
        Name = "Dashboard Final Settlement",
        VersionNo = 1,
        ScopeType = ApprovalScopeType.Business,
        EffectiveFrom = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    private static HrExitCase CreateDashboardExitCase(
        long businessId,
        long employeeId,
        long templateId,
        long locationId,
        long actorUserId,
        string suffix) => new()
    {
        BusinessId = businessId,
        EmployeeId = employeeId,
        ExitType = "Resignation",
        InitiatedDate = new DateOnly(2026, 10, 1),
        ProposedLastWorkingDate = new DateOnly(2026, 10, 31),
        ActualLastWorkingDate = new DateOnly(2026, 10, 31),
        Reason = "Dashboard acceptance fixture",
        Status = "Completed",
        InitiatedByUserId = actorUserId,
        InitiatedAtUtc = DateTime.UtcNow,
        PrimaryLocationProjectionIdSnapshot = locationId,
        LocationTypeSnapshot = "Outlet",
        ClearanceTemplateId = templateId,
        ClearanceTemplateVersionNo = 1,
        ClearanceTemplateSnapshotJson = $"{{\"fixture\":\"{suffix}\"}}",
        Version = 1
    };

    private static async Task<(PayrollFinalSettlement Settlement, PayrollFinalSettlementRevision Revision)>
        AddApprovedDashboardSettlementAsync(
            RetailPOSDbContext db,
            HrExitCase exitCase,
            long policyId,
            decimal netSettlement,
            decimal paidAmount,
            string key)
    {
        var settlement = new PayrollFinalSettlement
        {
            BusinessId = exitCase.BusinessId,
            ExitCaseId = exitCase.Id,
            EmployeeId = exitCase.EmployeeId,
            CurrentRevisionNo = 1,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = exitCase.InitiatedByUserId
        };
        db.PayrollFinalSettlements.Add(settlement);
        await db.SaveChangesAsync();

        var gross = Math.Max(0m, netSettlement);
        var recovery = Math.Max(0m, -netSettlement);
        var revision = new PayrollFinalSettlementRevision
        {
            BusinessId = settlement.BusinessId,
            FinalSettlementId = settlement.Id,
            RevisionNo = 1,
            SettlementDate = new DateOnly(2026, 10, 31),
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            GrossDue = gross,
            TotalDeductions = recovery,
            NetSettlement = netSettlement,
            CalculationInputHash = new string('I', 64),
            SnapshotHash = new string('S', 64),
            SnapshotJson = "{}",
            IdempotencyKey = $"dashboard-settlement-{key}",
            IdempotencyPayloadHash = new string('P', 64),
            CalculatedAtUtc = DateTime.UtcNow,
            CalculatedByUserId = exitCase.InitiatedByUserId
        };
        db.PayrollFinalSettlementRevisions.Add(revision);
        await db.SaveChangesAsync();

        var approval = new ApprovalInstance
        {
            BusinessId = settlement.BusinessId,
            PolicyId = policyId,
            PolicyCodeSnapshot = $"DASHBOARD-FINAL-{settlement.BusinessId}",
            PolicyVersionSnapshot = 1,
            ProcessType = ApprovalProcessType.FinalSettlement,
            TargetType = "PayrollFinalSettlement",
            TargetId = settlement.Id.ToString(),
            RequestedByUserId = exitCase.InitiatedByUserId,
            RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved,
            CurrentStepSequence = 1,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business,
            TargetVersion = settlement.Version,
            RequestHash = new string('A', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();

        db.PayrollFinalSettlementApprovalSubmissions.Add(new PayrollFinalSettlementApprovalSubmission
        {
            BusinessId = settlement.BusinessId,
            FinalSettlementId = settlement.Id,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            EmployeeId = settlement.EmployeeId,
            ExitCaseId = settlement.ExitCaseId,
            CalculationSnapshotHash = revision.SnapshotHash,
            ComponentLinesHash = new string('L', 64),
            GrossPayable = gross,
            TotalRecovery = recovery,
            NetSettlement = netSettlement,
            CurrencyCode = revision.CurrencyCode,
            SourceEvidenceHash = new string('E', 64),
            CalculationPolicyVersion = "1",
            ClearanceReferencesJson = "[]",
            ClearanceReferencesHash = new string('C', 64),
            SubmitterUserId = exitCase.InitiatedByUserId,
            SubmittedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"dashboard-submission-{key}",
            PayloadHash = new string('D', 64),
            SubmittedEvidenceJson = "{}",
            SubmittedEvidenceHash = new string('H', 64),
            ApprovalInstanceId = approval.Id
        });
        await db.SaveChangesAsync();

        if (paidAmount > 0m)
        {
            var payment = new PayrollPayment
            {
                BusinessId = settlement.BusinessId,
                FinalSettlementId = settlement.Id,
                FinalSettlementRevisionId = revision.Id,
                FinalSettlementRevisionNo = revision.RevisionNo,
                ApprovalInstanceId = approval.Id,
                HistoricalLocationIdSnapshot = exitCase.PrimaryLocationProjectionIdSnapshot,
                DecimalPrecisionSnapshot = 2,
                RoundingModeSnapshot = MidpointRounding.AwayFromZero.ToString(),
                FinalSettlementEvidenceHash = new string('H', 64),
                EmployeeId = settlement.EmployeeId,
                PayableAmount = netSettlement,
                PaidAmount = paidAmount,
                OutstandingAmount = netSettlement - paidAmount,
                CurrencyCode = "BDT",
                Method = PayrollPaymentMethods.Cash,
                Status = paidAmount == netSettlement
                    ? PayrollPaymentStatuses.Paid
                    : PayrollPaymentStatuses.PartiallyPaid,
                IdempotencyKey = $"dashboard-final-payment-{key}",
                IdempotencyPayloadHash = new string('W', 64),
                CreatedByUserId = exitCase.InitiatedByUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.PayrollPayments.Add(payment);
            await db.SaveChangesAsync();
            db.PayrollPaymentAllocations.Add(new PayrollPaymentAllocation
            {
                BusinessId = payment.BusinessId,
                PayrollPaymentId = payment.Id,
                AllocationNo = 1,
                Method = PayrollPaymentMethods.Cash,
                Amount = netSettlement,
                SettledAmount = paidAmount,
                State = paidAmount == netSettlement
                    ? PayrollPaymentAllocationStates.Settled
                    : PayrollPaymentAllocationStates.PartiallySettled
            });
            await db.SaveChangesAsync();
            db.PayrollPaymentEvidence.Add(CreateDashboardPaymentEvidence(
                payment, 100 + (int)payment.Id, DateTime.UtcNow, amount: paidAmount));
            await db.SaveChangesAsync();
        }

        return (settlement, revision);
    }

    private static async Task AddDashboardSettlementPostingAsync(
        RetailPOSDbContext db,
        PayrollFinalSettlement settlement,
        PayrollFinalSettlementRevision revision,
        string postingType)
    {
        db.PayrollFinalSettlementAccountingPostings.Add(new PayrollFinalSettlementAccountingPosting
        {
            BusinessId = settlement.BusinessId,
            FinalSettlementId = settlement.Id,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            PostingType = postingType,
            PostingReference = $"DASH-FINAL-{settlement.Id}",
            IdempotencyKey = $"dashboard-final-posting-{settlement.Id}",
            RequestPayloadJson = "{}",
            RequestPayloadHash = new string('F', 64),
            AccountingJournalId = 9000 + settlement.Id,
            Status = PayrollAccountingPostingStatuses.Posted,
            RequestedByUserId = settlement.CreatedByUserId,
            RequestedAtUtc = DateTime.UtcNow,
            PostedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
