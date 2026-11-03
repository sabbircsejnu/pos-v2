using Moq;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollFinalSettlementAccountingServiceTests
{
    [Theory]
    [InlineData(1000, 100, 900, 1000, 1000)]
    [InlineData(100, 200, -100, 200, 200)]
    public async Task PostsApprovedFrozenSettlementWithBalancedExpenseOrReceivableJournal(
        decimal earning, decimal deduction, decimal net, decimal expectedDebit, decimal expectedCredit)
    {
        var fixture = new PostingFixture(earning, deduction, net);

        var result = await fixture.Service.Handle(
            new PostPayrollFinalSettlementAccountingCommand(7, 1, "settlement-post-once"),
            CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, result.Status);
        Assert.Equal(55, result.AccountingJournalId);
        var input = fixture.Request!.ReadFrozenPayload<ManualJournalDraftInput>();
        Assert.Equal(expectedDebit, input.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(expectedCredit, input.Lines.Sum(line => line.CreditAmount));
        Assert.Equal(net > 0
                ? PayrollFinalSettlementPostingTypes.Expense
                : PayrollFinalSettlementPostingTypes.Receivable,
            result.PostingType);
        Assert.Equal("payroll.final_settlement.accounting.posting_requested",
            fixture.Audit.LastOperation?.ActionType);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ZeroValueApprovedSettlementDoesNotCreateJournalOrPosting()
    {
        var fixture = new PostingFixture(0, 0, 0);

        var result = await fixture.Service.Handle(
            new PostPayrollFinalSettlementAccountingCommand(7, 1, "zero-settlement"),
            CancellationToken.None);

        Assert.Equal("NoPostingRequired", result.Status);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Execution.Verify(store => store.SavePostingAsync(
            It.IsAny<PayrollFinalSettlementAccountingPosting>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoversCanonicalJournalWithoutPostingDuplicate()
    {
        var fixture = new PostingFixture(1000, 100, 900);
        fixture.Accounting.Setup(store => store.ResolveExistingAsync(
                It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(55);

        var result = await fixture.Service.Handle(
            new PostPayrollFinalSettlementAccountingCommand(7, 1, "settlement-post-recovery"),
            CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, result.Status);
        Assert.Equal(55, result.AccountingJournalId);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccountingDisabledDoesNotPersistPosting()
    {
        var fixture = new PostingFixture(1000, 100, 900, accountingEnabled: false);

        var result = await fixture.Service.Handle(
            new PostPayrollFinalSettlementAccountingCommand(7, 1, "accounting-disabled"),
            CancellationToken.None);

        Assert.Equal("NotEnabled", result.Status);
        fixture.Execution.Verify(store => store.SavePostingAsync(
            It.IsAny<PayrollFinalSettlementAccountingPosting>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Accounting.Verify(store => store.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class PostingFixture
    {
        private const string ExpenseReference = "FS-7-r1";
        private readonly Mock<IHrExitAccess> access = new();
        private readonly Mock<IPayrollFinalSettlementStore> settlements = new();
        private readonly Mock<IPayrollAccountingPostingStore> payrollAccounting = new();
        public readonly Mock<IPayrollFinalSettlementExecutionStore> Execution = new();
        private readonly Mock<IAccountingPostingAccountReadStore> accounts = new();
        public readonly Mock<IFrozenSystemJournalStore> Accounting = new();
        private readonly Mock<IJournalEntryReadStore> journals = new();
        public readonly RecordingAudit Audit = new();
        public readonly PayrollFinalSettlementAccountingService Service;
        public FrozenSystemJournalRequest? Request { get; private set; }

        public PostingFixture(
            decimal earning,
            decimal deduction,
            decimal net,
            bool accountingEnabled = true)
        {
            var source = new PayrollFinalSettlementModel(
                7, 8, 42, 1, new DateOnly(2026, 10, 11), "BDT", 2, "AwayFromZero",
                earning, deduction, net, new string('A', 64), new string('B', 64), "{}",
                new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), 101,
                [
                    new(1, "BASE", "Base salary", PayrollAccountingComponentTypes.Earning,
                        earning, earning != 0, null, "PayrollResult", "42", "{}"),
                    new(2, "RECOVERY", "Recovery", PayrollAccountingComponentTypes.Deduction,
                        deduction, deduction != 0, null, "PayrollResult", "42", "{}")
                ],
                Status: "Approved",
                ApprovalInstanceId: 90,
                SubmittedEvidenceHash: new string('C', 64))
            {
                RevisionId = 77
            };
            var accountRows = new[]
            {
                Account(501, ChartAccountTypes.Expense),
                Account(502, ChartAccountTypes.Liability),
                Account(503, ChartAccountTypes.Asset)
            };
            var mappingRows = new[]
            {
                Mapping(PayrollAccountingComponentTypes.Earning, "BASE", 501),
                Mapping(PayrollAccountingComponentTypes.Deduction, "RECOVERY", 502),
                Mapping(PayrollAccountingComponentTypes.NetPayable, "NET_PAYABLE", 502),
                Mapping(PayrollAccountingComponentTypes.FinalSettlementReceivable,
                    "FINAL_SETTLEMENT_RECEIVABLE", 503)
            };

            access.Setup(value => value.RequireBusinessId()).Returns(1);
            access.Setup(value => value.RequireRealUserId()).Returns(101);
            access.Setup(value => value.EnsureAsync(
                    1, HrExitOperation.FinalSettlementAccountingPost, 42, 10,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            settlements.Setup(value => value.GetSettlementScopeAsync(
                    1, 7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HrExitScope(42, 10, "Outlet"));
            settlements.Setup(value => value.GetByIdAsync(
                    1, 7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            payrollAccounting.Setup(value => value.IsAccountingEnabledAsync(
                    1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(accountingEnabled);
            payrollAccounting.Setup(value => value.GetMappingsAsync(
                    1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mappingRows.Select(row => new PayrollAccountingMappingReadModel(
                    row.ComponentType, row.ComponentCode, row.AccountId,
                    row.AccountId.ToString(), row.ComponentCode, row.IsActive, row.Version))
                    .ToArray());
            Execution.Setup(value => value.LockPostingIdentityAsync(
                    1, ExpenseReference, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Execution.Setup(value => value.FindPostingAsync(
                    1, 7, 1, It.IsAny<string>(), null, null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PayrollFinalSettlementAccountingPosting?)null);
            Execution.Setup(value => value.SavePostingAsync(
                    It.IsAny<PayrollFinalSettlementAccountingPosting>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            accounts.Setup(value => value.GetChartAccountsForBusinessAsync(
                    1, It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long _, IReadOnlyCollection<long> ids, CancellationToken _) =>
                    accountRows.Where(row => ids.Contains(row.Id)).ToArray());
            Accounting.Setup(value => value.ResolveExistingAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long?)null);
            Accounting.Setup(value => value.PostAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()))
                .Callback<FrozenSystemJournalRequest, long, CancellationToken>(
                    (request, _, _) => Request = request)
                .ReturnsAsync(55);
            journals.Setup(value => value.GetByIdAsync(
                    1, 55, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostedJournal());

            Service = new PayrollFinalSettlementAccountingService(
                access.Object, settlements.Object, payrollAccounting.Object,
                Execution.Object, accounts.Object, Accounting.Object, journals.Object, Audit);
        }

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

        private static ChartAccount Account(long id, string type) =>
            new()
            {
                Id = id,
                BusinessId = 1,
                AccountCode = id.ToString(),
                AccountName = $"Account {id}",
                AccountType = type,
                IsActive = true
            };

        private static JournalEntryReadModel PostedJournal() =>
            new(
                55, 1, "JRN-55", AccountingJournalTypes.SystemJournal,
                new DateTime(2026, 10, 11), new DateTime(2026, 10, 11),
                new DateTime(2026, 10, 11), null, null, "PayrollFinalSettlement",
                ExpenseReference, ExpenseReference, "Final settlement", null, null,
                null, null, JournalEntryStatuses.Posted, true, null, null, null, null,
                null, null, null, null, null, DateTime.UtcNow, 101, DateTime.UtcNow,
                101, DateTime.UtcNow, 101, null, null, null, null, null, [], []);
    }

    private sealed class RecordingAudit : IRequiredAuditTransaction
    {
        public RequiredAuditOperation? LastOperation { get; private set; }

        public Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            return action(cancellationToken);
        }

        public Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            Func<T, bool> requiresAudit,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            return action(cancellationToken);
        }
    }
}
