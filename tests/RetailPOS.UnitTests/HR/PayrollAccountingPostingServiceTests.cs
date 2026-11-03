using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollAccountingPostingServiceTests
{
    [Fact]
    public async Task PostUsesPersistedAmountsAndPeriodEndDateThroughFrozenAccountingContract()
    {
        var fixture = new PostingFixture();

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        Assert.Equal(55, response.Posting?.AccountingJournalId);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()), Times.Once);
        var input = Assert.IsType<ManualJournalDraftInput>(
            fixture.Request!.ReadFrozenPayload<ManualJournalDraftInput>());
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), input.PostingDate);
        Assert.Equal(3, input.SourceRevision);
        Assert.Equal("BDT", input.CurrencyCode);
        Assert.Equal(1000m, input.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(1000m, input.Lines.Sum(line => line.CreditAmount));
        Assert.Contains(input.Lines, line => line.AccountId == 1001 && line.DebitAmount == 1000m);
        Assert.Contains(input.Lines, line => line.AccountId == 2001 && line.CreditAmount == 100m);
        Assert.Contains(input.Lines, line => line.AccountId == 3001 && line.CreditAmount == 900m);
        Assert.All(input.Lines, line => Assert.Equal("HR_LOCATION:10", line.CostCenterId));
        Assert.Equal("payroll.accounting.post_requested", fixture.Audit.LastOperation?.ActionType);
    }

    [Fact]
    public async Task RecoversAccountingJournalBeforeRetryingExpensePosting()
    {
        var fixture = new PostingFixture();
        fixture.Accounting.Setup(value => value.ResolveExistingAsync(
                It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(55);

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        Assert.Equal(55, response.Posting?.AccountingJournalId);
        fixture.Accounting.Verify(value => value.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CorrectionRunFailsClosedWithoutCreatingAnAccountingJournal()
    {
        var fixture = new PostingFixture(runType: PayrollRunTypes.Correction);

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Null(response.Posting);
        Assert.Contains("No approved ADR-06", response.Message);
        Assert.Equal("payroll.accounting.correction_unconfigured",
            fixture.Audit.LastOperation?.ActionType);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CorrectionPostingRequiresTheExistingPayrollAccountingPermission()
    {
        var fixture = new PostingFixture(runType: PayrollRunTypes.Correction);
        fixture.Access.Setup(access => access.EnsurePermissionAsync(
                1, "payroll.accounting.post", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Forbidden."));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.PostAsync(17, CancellationToken.None));

        Assert.Null(fixture.Audit.LastOperation);
        fixture.Accounting.Verify(store => store.ReverseAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CorrectionPostingDoesNotCallAccountingWhenRequiredAuditFails()
    {
        var fixture = new PostingFixture(runType: PayrollRunTypes.Correction);
        fixture.Audit.Failure = new InvalidOperationException("Audit persistence failed.");
        fixture.Service = fixture.CreateService(
            new TestPayrollAccountingCorrectionPolicy());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.PostAsync(17, CancellationToken.None));

        fixture.Accounting.Verify(store => store.ReverseAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangedContentDoesNotReplaceAnAlreadyPostedIdentityWithConflictStatus()
    {
        var fixture = new PostingFixture(existingPostedConflict: true);

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        Assert.NotNull(response.Message);
        Assert.NotNull(response.Posting?.ConflictPayloadHash);
        Assert.NotEqual(new string('A', 64), response.Posting?.ConflictPayloadHash);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccountingDisabledBusinessDoesNotCreatePostingOrJournal()
    {
        var fixture = new PostingFixture(accountingEnabled: false);

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Null(response.Posting);
        Assert.Contains("not explicitly enabled", response.Message);
        fixture.Store.Verify(store => store.SavePostingAsync(
            It.IsAny<PayrollAccountingPosting>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeductionMappedToIncomeAccountFailsClosed()
    {
        var fixture = new PostingFixture(deductionAccountType: ChartAccountTypes.Income);

        var response = await fixture.Service.PostAsync(17, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Failed, response.Posting?.Status);
        Assert.Contains("must be mapped to an asset, liability, expense, or cost-of-goods-sold",
            response.Posting?.ErrorMessage);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class PostingFixture
    {
        private static readonly DateTime PeriodEnd = new(2026, 10, 31);
        private readonly PayrollAccountingSource source;

        public readonly Mock<IPayrollRunAccess> Access = new(MockBehavior.Strict);
        public readonly Mock<IHrPayrollCapabilityService> Capabilities = new();
        public readonly Mock<IPayrollAccountingPostingStore> Store = new(MockBehavior.Strict);
        public readonly Mock<IAccountingPostingAccountReadStore> Accounts = new(MockBehavior.Strict);
        public readonly Mock<IFrozenSystemJournalStore> Accounting = new(MockBehavior.Strict);
        public readonly Mock<IJournalEntryReadStore> Journals = new(MockBehavior.Strict);
        public readonly RecordingAudit Audit = new();
        public PayrollAccountingPostingService Service;
        public FrozenSystemJournalRequest? Request { get; private set; }

        public PostingFixture(
            string runType = PayrollRunTypes.Regular,
            bool accountingEnabled = true,
            bool existingPostedConflict = false,
            string deductionAccountType = ChartAccountTypes.Liability)
        {
            source = new PayrollAccountingSource(
                1, 8, 17, 2, runType, "RUN-2",
                runType == PayrollRunTypes.Correction ? "CORR-UNIT" : null,
                runType == PayrollRunTypes.Correction ? 16 : null,
                PayrollRunScopeTypes.Location, 10,
                PayrollRunStatuses.Approved, 3, "input-hash", "approval-hash", "BDT", 2,
                PayrollPeriodStatuses.Approved, new(2026, 10, 1), DateOnly.FromDateTime(PeriodEnd),
                null,
                [
                    new PayrollAccountingEmployeeSource(
                        91, 42, 10, "{\"frozen\":true}", "snapshot-hash", 1000m, 100m, 900m,
                        [
                            new(1, "BASE", PayrollAccountingComponentTypes.Earning, 1000m),
                            new(2, "TAX", PayrollAccountingComponentTypes.Deduction, 100m)
                        ])
                ]);

            Access.Setup(value => value.RequireBusinessId()).Returns(1);
            Access.Setup(value => value.RequireRealUserId()).Returns(101);
            Access.Setup(value => value.EnsurePermissionAsync(
                    1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Access.Setup(value => value.EnsureScopeAsync(
                    1, PayrollRunScopeTypes.Location, 10, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Capabilities.Setup(value => value.EnsureEnabledAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Store.Setup(value => value.FindSourceAsync(1, 17, It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            Store.Setup(value => value.GetMappingRowsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    Mapping(PayrollAccountingComponentTypes.Earning, "BASE", 1001),
                    Mapping(PayrollAccountingComponentTypes.Deduction, "TAX", 2001),
                    Mapping(PayrollAccountingComponentTypes.NetPayable, "NET_PAYABLE", 3001)
                ]);
            Store.Setup(value => value.IsAccountingEnabledAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(accountingEnabled);
            Store.Setup(value => value.LockPostingIdentityAsync(1, 17, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(value => value.FindPostingAsync(1, 17, 3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingPostedConflict
                    ? new PayrollAccountingPosting
                    {
                        Id = 4,
                        BusinessId = 1,
                        PayrollPeriodId = 8,
                        PayrollRunId = 17,
                        RunRevision = 3,
                        PostingReference = "payroll-expense:1:17:r3",
                        IdempotencyKey = "payroll-expense:1:17:r3",
                        RequestPayloadHash = new string('A', 64),
                        Status = PayrollAccountingPostingStatuses.Posted,
                        Version = 2
                    }
                    : null);
            Store.Setup(value => value.HasUnresolvedCorrectionAsync(1, 17, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Store.Setup(value => value.DiscardPendingChanges());
            Store.Setup(value => value.SavePostingAsync(
                    It.IsAny<PayrollAccountingPosting>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(value => value.TryMarkPeriodPostedAsync(
                    1, 8, 101, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Accounts.Setup(value => value.GetChartAccountsForBusinessAsync(
                    1, It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    Account(1001, "Payroll expense", ChartAccountTypes.Expense),
                    Account(2001, "Withholding payable", deductionAccountType),
                    Account(3001, "Salary payable", ChartAccountTypes.Liability)
                ]);

            Accounting.Setup(value => value.PostAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()))
                .Callback<FrozenSystemJournalRequest, long, CancellationToken>(
                    (request, _, _) => Request = request)
                .ReturnsAsync(55);
            Accounting.Setup(value => value.ResolveExistingAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long?)null);
            Journals.Setup(value => value.GetByIdAsync(1, 55, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostedJournal());
            Service = CreateService(new UnconfiguredPayrollAccountingCorrectionPolicy());
        }

        public PayrollAccountingPostingService CreateService(
            IPayrollAccountingCorrectionPolicy correctionPolicy) =>
            new(
                Access.Object, Capabilities.Object, Store.Object, Accounts.Object,
                Accounting.Object, Journals.Object, Audit,
                correctionPolicy,
                NullLogger<PayrollAccountingPostingService>.Instance);

        private static PayrollAccountingComponentMapping Mapping(
            string type, string code, long accountId) =>
            new()
            {
                BusinessId = 1,
                ComponentType = type,
                ComponentCode = code,
                AccountId = accountId,
                IsActive = true,
                Version = 1
            };

        private static ChartAccount Account(long id, string name, string type) =>
            new()
            {
                Id = id,
                BusinessId = 1,
                AccountCode = id.ToString(),
                AccountName = name,
                AccountType = type,
                IsActive = true
            };

        private JournalEntryReadModel PostedJournal() =>
            new(
                55, 1, "JRN-55", AccountingJournalTypes.SystemJournal, PeriodEnd,
                PeriodEnd, PeriodEnd, 2026, 10, "PayrollExpense",
                "payroll-expense:1:17:r3", "RUN-2", "Payroll expense accrual",
                null, null, null, null, JournalEntryStatuses.Posted, true,
                null, null, null, null, null, null, null, null, null,
                PeriodEnd, 101, PeriodEnd, 101, PeriodEnd, 101,
                null, null, null, null, null, [], []);
    }

    private sealed class RecordingAudit : IRequiredAuditTransaction
    {
        public RequiredAuditOperation? LastOperation { get; private set; }
        public Exception? Failure { get; set; }

        public Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            ExecuteAsync(operation, action, _ => true, cancellationToken);

        public async Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            Func<T, bool> requiresAudit,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            if (Failure is not null)
                throw Failure;
            return await action(cancellationToken);
        }
    }

    private sealed class TestPayrollAccountingCorrectionPolicy
        : IPayrollAccountingCorrectionPolicy
    {
        public Task<PayrollAccountingCorrectionPolicy> ResolveAsync(
            long businessId, string correctionReference, CancellationToken cancellationToken) =>
            Task.FromResult(new PayrollAccountingCorrectionPolicy(
                PayrollAccountingCorrectionStrategy.ReversalAndReplacement,
                "TEST-ADR-06-ReversalAndReplacement-v1"));
    }
}
