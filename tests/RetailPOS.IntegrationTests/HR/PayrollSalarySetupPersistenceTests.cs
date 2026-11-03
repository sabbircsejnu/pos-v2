using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollSalarySetupPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext auditContext = new();
    private readonly FailPayrollAudit failure = new();
    private RetailPOSDbContext db = null!;
    private PayrollSalaryHandlers handlers = null!;
    private PayrollSalarySetupStore store = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(
                new AuditChangeTrackerInterceptor(auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance),
                failure)
            .Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
            new Business { Id = 2, Name = "Other payroll" });
        await db.SaveChangesAsync();
        auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/payroll/v1/salary-components", null, null, null,
            false, null, null, null, null, "actor", "Payroll"));
        store = new(db);
        var scope = new Mock<IHrPayrollCapabilityScope>();
        scope.Setup(value => value.RequireBusinessId()).Returns(1);
        var capabilities = new Mock<IHrPayrollCapabilityService>();
        capabilities.Setup(value => value.EnsureEnabledAsync(
                HrPayrollCapabilityKeys.Payroll, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        handlers = new(scope.Object, capabilities.Object, store,
            new RequiredAuditTransaction(db, auditContext,
                new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance));
    }

    [Fact]
    public async Task FormulaGraphStructureOwnershipAndVersionConflictsAreEnforced()
    {
        var basePay = await SaveComponent(null, null, Component("BASE", "Fixed"));
        var allowance = await SaveComponent(null, null,
            Component("ALLOWANCE", "Formula", "BASE * 0.1"));
        var structure = await handlers.Handle(new SavePayrollSalaryStructureCommand(null, null,
            new PayrollSalaryStructureData("STANDARD", "Standard",
            [
                new(basePay.Id, 1000m, null, 1),
                new(allowance.Id, null, null, 2)
            ])), default);

        await Assert.ThrowsAsync<PayrollSalaryConflictException>(() =>
            SaveComponent(null, null, Component("BASE", "Fixed")));
        var updatedBase = await SaveComponent(
            basePay.Id, basePay.Version, basePay.Data with { Name = "Changed" });
        Assert.Equal(2, updatedBase.Version);
        await Assert.ThrowsAsync<PayrollSalaryConflictException>(() =>
            SaveComponent(basePay.Id, basePay.Version, basePay.Data));
        await Assert.ThrowsAsync<PayrollSalaryConflictException>(() =>
            SaveComponent(basePay.Id, updatedBase.Version, null));

        var foreignComponent = new PayrollSalaryComponent
        {
            BusinessId = 2,
            Code = "FOREIGN",
            Name = "Foreign",
            ComponentType = "Earning",
            ValueType = "Fixed"
        };
        db.PayrollSalaryComponents.Add(foreignComponent);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => handlers.Handle(
            new SavePayrollSalaryStructureCommand(null, null,
                new PayrollSalaryStructureData("FOREIGN_STRUCTURE", "Foreign structure",
                    [new(foreignComponent.Id, 1m, null, 1)])), default));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handlers.Handle(new SavePayrollSalaryComponentCommand(basePay.Id, updatedBase.Version,
                basePay.Data with { ValueType = "Formula", FormulaExpression = "ALLOWANCE + 1" }), default));
        Assert.Equal(2, (await store.ComponentAsync(1, basePay.Id, default)).Version);
        Assert.Equal(1, (await store.StructureAsync(1, structure.Id, default)).Version);
        Assert.True(await db.AuditEvents.CountAsync(row => row.BusinessId == 1) >= 4);
    }

    [Fact]
    public async Task RequiredAuditFailureRollsBackSalaryConfigurationWrite()
    {
        failure.Fail = true;

        await Assert.ThrowsAsync<IOException>(() =>
            SaveComponent(null, null, Component("BASE", "Fixed")));

        Assert.Empty(await db.PayrollSalaryComponents.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task InitialSalaryProposalIsFrozenIdempotentAndNotEffectiveBeforeApproval()
    {
        await HrRosterPersistenceTests.Seed(db, businessExists: true);
        var employee = await db.HrEmployees.SingleAsync(row => row.BusinessId == 1 && row.Id == 1);
        employee.CurrentEmploymentStatus = "Active";
        await db.SaveChangesAsync();

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

        var request = new CreatePayrollSalarySetupRequest(
            1, structure.Id, new DateOnly(2026, 10, 10),
            [new PayrollSalarySetupComponentInput(component.Id, 1250m)],
            "Initial hire", "salary-setup-once");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var created = await store.CreateInitialRequestAsync(1, 101, request, default);

        Assert.Equal("Draft", created.Status);
        Assert.Equal(1, created.SalaryStructureVersion);
        Assert.Equal(1250m, Assert.Single(created.Components).Value);
        Assert.Empty(await store.EmployeeSalaryHistoryAsync(1, 1, null, default));

        var replay = await store.CreateInitialRequestAsync(1, 101, request, default);
        Assert.True(replay.IsReplay);
        Assert.Equal(created.Id, replay.Id);
        await Assert.ThrowsAsync<PayrollSalaryConflictException>(() => store.CreateInitialRequestAsync(
            1, 101, request with { Reason = "Different payload" }, default));

        var savedRequest = await db.PayrollSalarySetupRequests.SingleAsync(row => row.Id == created.Id);
        savedRequest.ComponentSnapshotJson = "[]";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(savedRequest).ReloadAsync();

        var policy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "SALARY_TEST",
            ProcessType = ApprovalProcessType.SalarySetup,
            Name = "Salary setup test policy",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new DateOnly(2026, 1, 1),
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
        var approval = new ApprovalInstance
        {
            BusinessId = 1,
            PolicyId = policy.Id,
            PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = policy.VersionNo,
            ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = PayrollSalarySetupApprovalConstants.TargetType,
            TargetId = created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Location,
            ScopeReference = created.OperationalLocationProjectionId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            TargetVersion = 1,
            RequestHash = "approved-snapshot-test"
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();
        savedRequest.Status = "PendingApproval";
        savedRequest.ApprovalInstanceId = approval.Id;
        savedRequest.SubmittedByUserId = 101;
        savedRequest.SubmittedAtUtc = DateTime.UtcNow;
        savedRequest.SubmittedTargetVersion = 1;
        savedRequest.Version = 2;
        await db.SaveChangesAsync();

        await store.DecisionCompletedAsync(1, approval, 202, "Approved", default);
        await db.SaveChangesAsync();

        var salary = Assert.Single(await store.EmployeeSalaryHistoryAsync(1, 1, null, default));
        Assert.Equal(created.EffectiveFrom, salary.EffectiveFrom);
        Assert.Equal(1250m, Assert.Single(salary.Components).Value);
        Assert.Equal(approval.Id, salary.ApprovalInstanceId);

        var revisionStore = new PayrollSalaryRevisionStore(db);
        var revisionInput = new CreatePayrollSalaryRevisionRequest(
            1, salary.Id, new DateOnly(2026, 10, 20),
            [new PayrollSalarySetupComponentInput(component.Id, 1500m)],
            "Annual adjustment", "salary-revision-once");
        var revision = await revisionStore.CreateAsync(1, 101, revisionInput, default);
        Assert.Equal("Draft", revision.Status);
        Assert.Equal(1250m, Assert.Single(revision.CurrentComponents).Value);
        Assert.Equal(1500m, Assert.Single(revision.ProposedComponents).Value);
        Assert.Single(await store.EmployeeSalaryHistoryAsync(
            1, 1, new DateOnly(2026, 10, 19), default));
        var revisionReplay = await revisionStore.CreateAsync(1, 101, revisionInput, default);
        Assert.True(revisionReplay.IsReplay);
        Assert.Equal(revision.Id, revisionReplay.Id);
        await Assert.ThrowsAsync<PayrollSalaryConflictException>(() => revisionStore.CreateAsync(
            1, 101, revisionInput with { Reason = "Different adjustment" }, default));

        var revisionRequest = await db.PayrollSalaryRevisionRequests.SingleAsync(row => row.Id == revision.Id);
        revisionRequest.ProposedComponentSnapshotJson = "[]";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(revisionRequest).ReloadAsync();

        var revisionPolicy = new ApprovalPolicy
        {
            BusinessId = 1,
            Code = "SALARY_REVISION_TEST",
            ProcessType = ApprovalProcessType.SalaryRevision,
            Name = "Salary revision test policy",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        db.ApprovalPolicies.Add(revisionPolicy);
        await db.SaveChangesAsync();
        var revisionApproval = new ApprovalInstance
        {
            BusinessId = 1,
            PolicyId = revisionPolicy.Id,
            PolicyCodeSnapshot = revisionPolicy.Code,
            PolicyVersionSnapshot = revisionPolicy.VersionNo,
            ProcessType = ApprovalProcessType.SalaryRevision,
            TargetType = PayrollSalaryRevisionApprovalConstants.TargetType,
            TargetId = revision.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Location,
            ScopeReference = revision.OperationalLocationProjectionId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            TargetVersion = revision.Version,
            RequestHash = "approved-revision-snapshot-test"
        };
        db.ApprovalInstances.Add(revisionApproval);
        await db.SaveChangesAsync();
        revisionRequest.Status = "PendingApproval";
        revisionRequest.ApprovalInstanceId = revisionApproval.Id;
        revisionRequest.SubmittedByUserId = 101;
        revisionRequest.SubmittedAtUtc = DateTime.UtcNow;
        revisionRequest.SubmittedTargetVersion = revision.Version;
        revisionRequest.Version = checked(revision.Version + 1);
        await db.SaveChangesAsync();
        await revisionStore.DecisionCompletedAsync(1, revisionApproval, 202, "Approved", default);
        await db.SaveChangesAsync();

        var salaryAfterRevision = await store.EmployeeSalaryHistoryAsync(1, 1, null, default);
        Assert.Equal(2, salaryAfterRevision.Count);
        Assert.Equal(1500m, salaryAfterRevision.OrderByDescending(row => row.EffectiveFrom)
            .First().Components.Single(row => row.Code == "BASE").Value);
        var approvedSuccessor = await db.PayrollEmployeeSalaryVersions.SingleAsync(
            row => row.SalaryRevisionRequestId == revision.Id);
        Assert.Equal(salary.Id, approvedSuccessor.PredecessorSalaryVersionId);
        Assert.Equal(salary.SalarySetupRequestId, approvedSuccessor.SalarySetupRequestId);

        var savedSalary = await db.PayrollEmployeeSalaryVersions.SingleAsync(row => row.Id == salary.Id);
        savedSalary.ComponentSnapshotJson = "[]";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.True(AuditFieldRedaction.IsSensitive(
            nameof(PayrollEmployeeSalaryVersion), nameof(PayrollEmployeeSalaryVersion.ComponentSnapshotJson)));
        await transaction.RollbackAsync();
    }

    private Task<PayrollSalaryComponentResponse> SaveComponent(
        long? id, long? version, PayrollSalaryComponentData? data) =>
        handlers.Handle(new SavePayrollSalaryComponentCommand(id, version, data), default);

    private static PayrollSalaryComponentData Component(
        string code, string valueType, string? formula = null) =>
        new(code, code, "Earning", valueType, null, true, 1, formula);

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class FailPayrollAudit : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<AuditEvent>()
                    .Any(entry => entry.State == EntityState.Added))
                throw new IOException("Injected Payroll audit persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
