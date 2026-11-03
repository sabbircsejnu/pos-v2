using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Exit;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrExitPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext db = null!;
    private HrExitStore store = null!;
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly LastWorkingDate = new(2026, 10, 30);

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Exit test" });
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent"
        });
        db.OperationalLocationProjections.Add(new OperationalLocationProjection
        {
            Id = 10, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
            SourceEntityId = "10", Name = "Outlet 10", SourceVersion = 1
        });
        db.HrEmployees.Add(new HrEmployee
        {
            Id = 1, BusinessId = 1, EmployeeCode = "E-1", FirstName = "Test",
            DisplayName = "Test employee", CurrentEmploymentTypeId = 1,
            CurrentPrimaryLocationProjectionId = 10, JoiningDate = Start
        });
        db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 1, EmployeeId = 1, EmploymentTypeId = 1, StartDate = Start, Status = "Active"
        });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 10,
            LocationNameSnapshot = "Outlet 10", SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "10",
            LocationSourceVersionSnapshot = 1, EffectiveFrom = Start,
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        store = new HrExitStore(db, TimeProvider.System);
    }

    [Fact]
    public async Task ExitSnapshotsClearanceAndEmployeeClosureAreVersionedAndImmutable()
    {
        var broad = await store.CreateTemplateAsync(1, 101, Template("BUSINESS", HrClearanceTemplateScopeType.Business, null), default);
        var type = await store.CreateTemplateAsync(1, 101, Template("OUTLET", HrClearanceTemplateScopeType.LocationType, "Outlet"), default);
        var location = await store.CreateTemplateAsync(1, 101, Template("OUTLET-10", HrClearanceTemplateScopeType.Location, "10"), default);
        Assert.NotEqual(broad.Id, type.Id);
        Assert.NotEqual(type.Id, location.Id);

        var exit = await store.InitiateAsync(1, 101,
            new HrExitCaseInput(1, "Resignation", new DateOnly(2026, 10, 1), LastWorkingDate, "Notice received."),
            default);
        Assert.Equal(location.Id, exit.ClearanceTemplateId);
        Assert.Equal(1, exit.ClearanceTemplateVersionNo);
        var item = Assert.Single(exit.ClearanceItems);
        Assert.Equal("ASSET", item.Code);
        Assert.Equal("Draft", exit.Status);
        Assert.Single(exit.Events);

        db.ChangeTracker.Clear();
        var mutableCase = await db.HrExitCases.SingleAsync(row => row.Id == exit.Id);
        mutableCase.Reason = "Edited";
        mutableCase.Version++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await db.Entry(mutableCase).ReloadAsync();

        var exception = await store.UpdateClearanceAsync(1, 102,
            new HrClearanceUpdateInput(item.Id, item.Version, "Exception", "Asset not returned.", null), default);
        Assert.Equal(2, exception!.Version);
        await store.ApproveWithoutApprovalAsync(1, 101, exit.Id, exception.Version, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteNonFinancialAsync(
            1, 101, exit.Id, exception.Version + 1, LastWorkingDate, "Exit completed.", default));

        var approved = await store.GetAsync(1, exit.Id, default);
        var exceptionItem = Assert.Single(approved!.ClearanceItems);
        var notApplicable = await store.UpdateClearanceAsync(1, 102,
            new HrClearanceUpdateInput(exceptionItem.Id, exceptionItem.Version, "NotApplicable",
                "Asset was assigned to another employee.", null), default);
        var completed = await store.CompleteNonFinancialAsync(1, 101, exit.Id, notApplicable!.Version,
            LastWorkingDate, "Clearance confirmed.", default);

        Assert.Equal("Completed", completed!.Status);
        Assert.Equal(LastWorkingDate, completed.ActualLastWorkingDate);
        Assert.Contains(completed.Events, row => row.EventType == "Completed");
        Assert.Equal("Exited", (await db.HrEmployees.SingleAsync(row => row.Id == 1)).CurrentEmploymentStatus);
        var period = await db.HrEmploymentPeriods.SingleAsync(row => row.EmployeeId == 1);
        Assert.Equal("Closed", period.Status);
        Assert.Equal(LastWorkingDate, period.EndDate);
        Assert.Equal(0, await db.HrExitCases.CountAsync(row => row.BusinessId == 2));

        db.ChangeTracker.Clear();
        var evidence = await db.HrExitCaseEvents.FirstAsync(row => row.ExitCaseId == exit.Id);
        evidence.Reason = "Altered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task NonApplicableAndExceptionMustHaveReasonAndFutureTemplatesCannotOverlap()
    {
        await store.CreateTemplateAsync(1, 101,
            Template("BASE", HrClearanceTemplateScopeType.Business, null), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateTemplateAsync(1, 101,
            Template("BASE", HrClearanceTemplateScopeType.Business, null), default));
        var exit = await store.InitiateAsync(1, 101,
            new HrExitCaseInput(1, "Retirement", new DateOnly(2026, 10, 1), LastWorkingDate, "Retirement."),
            default);
        var item = Assert.Single(exit.ClearanceItems);
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateClearanceAsync(1, 101,
            new HrClearanceUpdateInput(item.Id, item.Version, "NotApplicable", null, null), default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateClearanceAsync(1, 101,
            new HrClearanceUpdateInput(item.Id, item.Version, "Exception", "  ", null), default));
    }

    [Fact]
    public async Task HrOnlyCompletionPersistsImmutableEvidenceWhenPayrollIsDisabled()
    {
        var approved = await CreateApprovedExitAsync();

        var preview = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(preview);
        Assert.False(preview.PayrollEnabled);
        Assert.True(preview.CanComplete);
        Assert.Null(preview.Settlement);

        var completed = await store.CompleteNonFinancialAsync(
            1, 101, approved.Id, approved.Version, LastWorkingDate, "HR clearance complete.", default);

        Assert.NotNull(completed);
        Assert.Equal("Completed", completed!.Status);
        var evidence = await db.HrExitCompletionEvidence.SingleAsync(row => row.ExitCaseId == approved.Id);
        Assert.False(evidence.PayrollEnabled);
        Assert.Equal("HrOnly", evidence.CompletionMode);
        Assert.Null(evidence.FinalSettlementId);
        Assert.NotEqual(string.Empty, evidence.EvidenceHash);

        db.ChangeTracker.Clear();
        var mutable = await db.HrExitCompletionEvidence.SingleAsync(row => row.ExitCaseId == approved.Id);
        mutable.EvidenceJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PositiveSettlementCompletionRequiresPaymentReconciliationAndAccounting()
    {
        await SetFeatureAsync("payroll.enabled", true);
        await SetFeatureAsync("accounting.enabled", true);
        var approved = await CreateApprovedExitAsync();
        var settlement = await SeedApprovedSettlementAsync(approved.Id, 1000m);

        var blocked = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(blocked);
        Assert.False(blocked!.CanComplete);
        Assert.Equal("PositiveSettlement", blocked.Settlement!.CompletionMode);
        Assert.Contains(blocked.Blockers,
            blocker => blocker.Contains("verified payment evidence", StringComparison.OrdinalIgnoreCase));

        await SeedSatisfiedPositiveSettlementAsync(settlement, 1000m);

        var ready = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(ready);
        Assert.True(ready!.CanComplete);
        Assert.Equal("PositiveSettlement", ready.Settlement!.CompletionMode);
        Assert.True(ready.Settlement.PaymentsSatisfied);
        Assert.True(ready.Settlement.ReconciliationSatisfied);
        Assert.True(ready.Settlement.AccountingSatisfied);

        var completed = await store.CompleteNonFinancialAsync(
            1, 101, approved.Id, approved.Version, LastWorkingDate, "Financial obligations closed.", default);

        Assert.NotNull(completed);
        var evidence = await db.HrExitCompletionEvidence.SingleAsync(row => row.ExitCaseId == approved.Id);
        Assert.Equal(settlement.FinalSettlementId, evidence.FinalSettlementId);
        Assert.Equal(settlement.RevisionId, evidence.FinalSettlementRevisionId);
        Assert.Equal(settlement.RevisionNo, evidence.FinalSettlementRevisionNo);
        Assert.Equal(1000m, evidence.NetSettlement);
        Assert.Equal("Approved", evidence.FinalSettlementStatus);
        Assert.Equal(new string('S', 64), evidence.FinalSettlementEvidenceHash);
    }

    [Fact]
    public async Task ZeroSettlementCompletionDisallowsOutgoingPaymentAttempts()
    {
        await SetFeatureAsync("payroll.enabled", true);
        var approved = await CreateApprovedExitAsync();
        var settlement = await SeedApprovedSettlementAsync(approved.Id, 0m);

        var ready = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(ready);
        Assert.True(ready!.CanComplete);
        Assert.Equal("ZeroSettlement", ready.Settlement!.CompletionMode);

        await AddPaymentAttemptAsync(settlement, 100m, 0m, 100m, PayrollPaymentStatuses.Pending);

        var blocked = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(blocked);
        Assert.False(blocked!.CanComplete);
        Assert.Contains(blocked.Blockers,
            blocker => blocker.Contains("must not create outgoing payment attempts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NegativeSettlementCompletionRequiresPostedReceivable()
    {
        await SetFeatureAsync("payroll.enabled", true);
        await SetFeatureAsync("accounting.enabled", true);
        var approved = await CreateApprovedExitAsync();
        var settlement = await SeedApprovedSettlementAsync(approved.Id, -250m);

        var blocked = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(blocked);
        Assert.False(blocked!.CanComplete);
        Assert.Equal("NegativeSettlement", blocked.Settlement!.CompletionMode);
        Assert.Contains(blocked.Blockers,
            blocker => blocker.Contains("receivable", StringComparison.OrdinalIgnoreCase));

        await AddReceivablePostingAsync(settlement);

        var ready = await store.GetCompletionPreviewAsync(1, approved.Id, default);
        Assert.NotNull(ready);
        Assert.True(ready!.CanComplete);
        Assert.Equal(PayrollFinalSettlementPostingTypes.Receivable, ready.Settlement!.RequiredPostingType);

        var completed = await store.CompleteNonFinancialAsync(
            1, 101, approved.Id, approved.Version, LastWorkingDate, "Receivable acknowledged.", default);

        Assert.NotNull(completed);
        var evidence = await db.HrExitCompletionEvidence.SingleAsync(row => row.ExitCaseId == approved.Id);
        Assert.Equal("NegativeSettlement", evidence.CompletionMode);
        Assert.Equal(-250m, evidence.NetSettlement);
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static HrClearanceTemplateInput Template(
        string code, HrClearanceTemplateScopeType scopeType, string? scopeReference) =>
        new(code, code, scopeType, scopeReference, 1, Start, null, true,
        [new HrClearanceTemplateItemInput(1, "ASSET", "Return assets", "Operations", null, true)]);

    private async Task<HrExitCaseModel> CreateApprovedExitAsync()
    {
        await store.CreateTemplateAsync(1, 101,
            Template($"EXIT-{Guid.NewGuid():N}", HrClearanceTemplateScopeType.Business, null), default);
        var exit = await store.InitiateAsync(1, 101,
            new HrExitCaseInput(1, "Resignation", new DateOnly(2026, 10, 1), LastWorkingDate, "Notice received."),
            default);
        var item = Assert.Single(exit.ClearanceItems);
        var cleared = await store.UpdateClearanceAsync(1, 102,
            new HrClearanceUpdateInput(item.Id, item.Version, "Cleared", null, "ASSET-RETURNED"), default);
        Assert.NotNull(cleared);
        await store.ApproveWithoutApprovalAsync(1, 101, exit.Id, cleared!.Version, default);
        var approved = await store.GetAsync(1, exit.Id, default);
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved!.Status);
        return approved;
    }

    private async Task SetFeatureAsync(string featureKey, bool enabled)
    {
        var setting = await db.BusinessFeatureSettings.SingleOrDefaultAsync(row =>
            row.BusinessId == 1 && row.FeatureKey == featureKey);
        if (setting is null)
        {
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1,
                FeatureKey = featureKey,
                IsEnabled = enabled
            });
        }
        else
        {
            setting.IsEnabled = enabled;
        }

        await db.SaveChangesAsync();
    }

    private async Task<SeededSettlement> SeedApprovedSettlementAsync(long exitCaseId, decimal netSettlement)
    {
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
            SettlementDate = LastWorkingDate,
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            GrossDue = netSettlement >= 0m ? netSettlement : 0m,
            TotalDeductions = netSettlement >= 0m ? 0m : Math.Abs(netSettlement),
            NetSettlement = netSettlement,
            CalculationInputHash = new string('A', 64),
            SnapshotHash = new string('B', 64),
            SnapshotJson = "{}",
            IdempotencyKey = $"settlement-{Guid.NewGuid():N}",
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
                    LineType = netSettlement >= 0m
                        ? PayrollFinalSettlementLineTypes.Earning
                        : PayrollFinalSettlementLineTypes.Deduction,
                    Amount = Math.Abs(netSettlement),
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

        return new SeededSettlement(settlement.Id, revision.Id, revision.RevisionNo, approval.Id);
    }

    private async Task SeedSatisfiedPositiveSettlementAsync(SeededSettlement settlement, decimal amount)
    {
        var payment = await AddPaymentAttemptAsync(
            settlement, amount, amount, 0m, PayrollPaymentStatuses.Paid,
            PayrollPaymentAllocationStates.Settled);
        var evidence = new PayrollPaymentEvidence
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            Type = PayrollPaymentEvidenceTypes.Settlement,
            Amount = amount,
            EvidenceDate = LastWorkingDate,
            EvidenceReference = "FS-SETTLED",
            AllocationEvidenceJson = $$"""[{"AllocationNo":1,"Method":"Cash","Amount":{{amount}}}]""",
            IdempotencyKey = $"evidence-{Guid.NewGuid():N}",
            PayloadHash = new string('H', 64),
            VerifiedByUserId = 101,
            VerifiedAtUtc = DateTime.UtcNow
        };
        db.PayrollPaymentEvidence.Add(evidence);
        await db.SaveChangesAsync();

        var allocationId = await db.PayrollPaymentAllocations
            .Where(row => row.PayrollPaymentId == payment.Id)
            .Select(row => row.Id)
            .SingleAsync();
        db.PayrollPaymentReconciliations.Add(new PayrollPaymentReconciliation
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            PayrollPaymentAllocationId = allocationId,
            Version = 1,
            PaymentVersion = 1,
            ExpectedAmount = amount,
            SettledAmount = amount,
            Difference = 0m,
            Status = PayrollPaymentReconciliationStatuses.Reconciled,
            SettlementDate = LastWorkingDate,
            EvidenceSource = PayrollPaymentMethods.Cash,
            ExternalReference = "FS-SETTLED",
            IdempotencyKey = $"recon-{Guid.NewGuid():N}",
            PayloadHash = new string('Q', 64),
            ReconciledByUserId = 101,
            ReconciledAtUtc = DateTime.UtcNow
        });
        db.PayrollFinalSettlementAccountingPostings.AddRange(
            new PayrollFinalSettlementAccountingPosting
            {
                BusinessId = 1,
                FinalSettlementId = settlement.FinalSettlementId,
                RevisionId = settlement.RevisionId,
                RevisionNo = settlement.RevisionNo,
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
                FinalSettlementId = settlement.FinalSettlementId,
                RevisionId = settlement.RevisionId,
                RevisionNo = settlement.RevisionNo,
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
        await db.SaveChangesAsync();
    }

    private async Task AddReceivablePostingAsync(SeededSettlement settlement)
    {
        db.PayrollFinalSettlementAccountingPostings.Add(new PayrollFinalSettlementAccountingPosting
        {
            BusinessId = 1,
            FinalSettlementId = settlement.FinalSettlementId,
            RevisionId = settlement.RevisionId,
            RevisionNo = settlement.RevisionNo,
            PostingType = PayrollFinalSettlementPostingTypes.Receivable,
            PostingReference = "FS-RECEIVABLE",
            IdempotencyKey = $"receivable-{Guid.NewGuid():N}",
            RequestPayloadJson = "{}",
            RequestPayloadHash = new string('Z', 64),
            AccountingJournalId = 9010,
            Status = PayrollAccountingPostingStatuses.Posted,
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            PostedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<PayrollPayment> AddPaymentAttemptAsync(
        SeededSettlement settlement,
        decimal payableAmount,
        decimal paidAmount,
        decimal outstandingAmount,
        string status,
        string allocationState = PayrollPaymentAllocationStates.Pending)
    {
        var payment = new PayrollPayment
        {
            BusinessId = 1,
            FinalSettlementId = settlement.FinalSettlementId,
            FinalSettlementRevisionId = settlement.RevisionId,
            FinalSettlementRevisionNo = settlement.RevisionNo,
            ApprovalInstanceId = settlement.ApprovalInstanceId,
            HistoricalLocationIdSnapshot = 10,
            DecimalPrecisionSnapshot = 2,
            RoundingModeSnapshot = MidpointRounding.AwayFromZero.ToString(),
            FinalSettlementEvidenceHash = new string('S', 64),
            EmployeeId = 1,
            PayableAmount = payableAmount,
            PaidAmount = paidAmount,
            OutstandingAmount = outstandingAmount,
            CurrencyCode = "BDT",
            Method = PayrollPaymentMethods.Cash,
            Status = status,
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
            Amount = payableAmount,
            SettledAmount = paidAmount,
            State = allocationState
        });
        await db.SaveChangesAsync();
        return payment;
    }

    private sealed record SeededSettlement(
        long FinalSettlementId,
        long RevisionId,
        int RevisionNo,
        long ApprovalInstanceId);
}
