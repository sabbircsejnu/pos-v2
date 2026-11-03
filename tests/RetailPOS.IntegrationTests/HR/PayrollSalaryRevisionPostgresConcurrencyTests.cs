using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollSalaryRevisionPostgresConcurrencyTests
{
    [Fact]
    public async Task ConcurrentApprovalCallbacksCreateOneEffectiveSuccessor()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        long employeeId;
        long sourceSalaryVersionId;
        long revisionRequestId;
        long revisionApprovalId;
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var componentJson = JsonSerializer.Serialize(
            new[]
            {
                new PayrollSalarySetupComponentSnapshot(
                    1, "BASE", "Base salary", "Earning", "Fixed", null, true, 1, null, 1000m)
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await using (var db = CreateDb(database.ConnectionString))
        {
            await db.Database.MigrateAsync();
            await HrRosterPersistenceTests.Seed(db);
            employeeId = await db.HrEmployees.Where(row => row.BusinessId == 1)
                .Select(row => row.Id).SingleAsync();

            var component = new PayrollSalaryComponent
            {
                BusinessId = 1,
                Code = "BASE",
                Name = "Base salary",
                ComponentType = "Earning",
                ValueType = "Fixed",
                IsRecurring = true,
                CalculationOrder = 1
            };
            db.PayrollSalaryComponents.Add(component);
            await db.SaveChangesAsync();

            var structure = new PayrollSalaryStructure
            {
                BusinessId = 1,
                Code = "STANDARD",
                Name = "Standard",
                Lines =
                [
                    new PayrollSalaryStructureLine
                    {
                        BusinessId = 1,
                        SalaryComponentId = component.Id,
                        DefaultValue = 1000m,
                        CalculationOrder = 1
                    }
                ]
            };
            db.PayrollSalaryStructures.Add(structure);
            await db.SaveChangesAsync();

            var setupPolicy = Policy("SALARY_SETUP_CONCURRENCY", ApprovalProcessType.SalarySetup);
            var revisionPolicy = Policy("SALARY_REVISION_CONCURRENCY", ApprovalProcessType.SalaryRevision);
            db.ApprovalPolicies.AddRange(setupPolicy, revisionPolicy);
            await db.SaveChangesAsync();

            var setupApproval = Approval(
                setupPolicy, ApprovalProcessType.SalarySetup, "salary-setup", "a");
            db.ApprovalInstances.Add(setupApproval);
            await db.SaveChangesAsync();

            var setupRequest = new PayrollSalarySetupRequest
            {
                BusinessId = 1,
                EmployeeId = employeeId,
                SalaryStructureId = structure.Id,
                SalaryStructureVersion = structure.Version,
                EffectiveFrom = effectiveFrom.AddDays(-1),
                OperationalLocationProjectionId = 10,
                StructureSnapshotJson = """{"Code":"STANDARD","Name":"Standard","Lines":[]}""",
                ComponentSnapshotJson = componentJson,
                RequestHash = new string('b', 64),
                IdempotencyPayloadHash = new string('c', 64),
                IdempotencyKey = "initial-salary",
                Reason = "Initial salary",
                Status = "Approved",
                ApprovalInstanceId = setupApproval.Id,
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow,
                DecidedByUserId = 202,
                DecidedAtUtc = DateTime.UtcNow
            };
            db.PayrollSalarySetupRequests.Add(setupRequest);
            await db.SaveChangesAsync();

            var sourceSalary = new PayrollEmployeeSalaryVersion
            {
                BusinessId = 1,
                EmployeeId = employeeId,
                SalarySetupRequestId = setupRequest.Id,
                SalaryStructureId = structure.Id,
                SalaryStructureVersion = structure.Version,
                EffectiveFrom = setupRequest.EffectiveFrom,
                OperationalLocationProjectionId = 10,
                StructureSnapshotJson = setupRequest.StructureSnapshotJson,
                ComponentSnapshotJson = componentJson,
                ApprovalInstanceId = setupApproval.Id,
                ApprovedByUserId = 202,
                ApprovedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.PayrollEmployeeSalaryVersions.Add(sourceSalary);
            await db.SaveChangesAsync();
            sourceSalaryVersionId = sourceSalary.Id;

            var revision = new PayrollSalaryRevisionRequest
            {
                BusinessId = 1,
                EmployeeId = employeeId,
                SourceSalaryVersionId = sourceSalary.Id,
                EffectiveFrom = effectiveFrom,
                OperationalLocationProjectionId = 10,
                StructureSnapshotJson = setupRequest.StructureSnapshotJson,
                CurrentComponentSnapshotJson = componentJson,
                ProposedComponentSnapshotJson = JsonSerializer.Serialize(
                    new[]
                    {
                        new PayrollSalarySetupComponentSnapshot(
                            1, "BASE", "Base salary", "Earning", "Fixed", null, true, 1, null, 1500m)
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                RequestHash = new string('d', 64),
                IdempotencyPayloadHash = new string('e', 64),
                IdempotencyKey = "salary-revision-concurrency",
                Reason = "Concurrent revision",
                Status = "Draft",
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow,
                Version = 1
            };
            db.PayrollSalaryRevisionRequests.Add(revision);
            await db.SaveChangesAsync();
            revisionRequestId = revision.Id;

            var approval = Approval(
                revisionPolicy, ApprovalProcessType.SalaryRevision,
                PayrollSalaryRevisionApprovalConstants.TargetType, revision.Id.ToString());
            approval.Status = ApprovalInstanceStatus.Approved;
            db.ApprovalInstances.Add(approval);
            await db.SaveChangesAsync();
            revision.ApprovalInstanceId = approval.Id;
            revision.Status = "PendingApproval";
            revision.SubmittedByUserId = 101;
            revision.SubmittedAtUtc = DateTime.UtcNow;
            revision.SubmittedTargetVersion = 1;
            revision.Version = 2;
            await db.SaveChangesAsync();
            revisionApprovalId = approval.Id;
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> ApplyDecisionAsync()
        {
            await start.Task;
            await using var db = CreateDb(database.ConnectionString);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var approval = await db.ApprovalInstances.AsNoTracking()
                .SingleAsync(row => row.BusinessId == 1 && row.Id == revisionApprovalId);
            try
            {
                await new PayrollSalaryRevisionStore(db).DecisionCompletedAsync(
                    1, approval, 202, "Approved", CancellationToken.None);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch (ApprovalKernelConflictException)
            {
                return false;
            }
        }

        var first = ApplyDecisionAsync();
        var second = ApplyDecisionAsync();
        start.SetResult();
        var applied = await Task.WhenAll(first, second);

        Assert.Single(applied, result => result);
        Assert.Single(applied, result => !result);

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Equal(2, await verify.PayrollEmployeeSalaryVersions.CountAsync(
            row => row.BusinessId == 1 && row.EmployeeId == employeeId));
        var successor = await verify.PayrollEmployeeSalaryVersions.SingleAsync(
            row => row.BusinessId == 1 && row.SalaryRevisionRequestId == revisionRequestId);
        Assert.Equal(sourceSalaryVersionId, successor.PredecessorSalaryVersionId);
        Assert.Equal(1500m, Assert.Single(JsonSerializer.Deserialize<PayrollSalarySetupComponentSnapshot[]>(
            successor.ComponentSnapshotJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!).Value);
        Assert.Equal("Approved", await verify.PayrollSalaryRevisionRequests
            .Where(row => row.Id == revisionRequestId).Select(row => row.Status).SingleAsync());
    }

    private static RetailPOSDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private static ApprovalPolicy Policy(string code, ApprovalProcessType processType) => new()
    {
        BusinessId = 1,
        Code = code,
        ProcessType = processType,
        Name = code,
        VersionNo = 1,
        ScopeType = ApprovalScopeType.Business,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true
    };

    private static ApprovalInstance Approval(
        ApprovalPolicy policy, ApprovalProcessType processType, string targetType, string targetId) => new()
    {
        BusinessId = 1,
        PolicyId = policy.Id,
        PolicyCodeSnapshot = policy.Code,
        PolicyVersionSnapshot = policy.VersionNo,
        ProcessType = processType,
        TargetType = targetType,
        TargetId = targetId,
        RequestedByUserId = 101,
        RequestedAtUtc = DateTime.UtcNow,
        Status = ApprovalInstanceStatus.Approved,
        CorrelationId = Guid.NewGuid(),
        ScopeType = ApprovalScopeType.Location,
        ScopeReference = "10",
        TargetVersion = 1,
        RequestHash = new string('a', 64)
    };
}
