using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollAccountingSettlementPostingServiceTests
{
    [Theory]
    [InlineData(PayrollPaymentMethods.Cash, 1001)]
    [InlineData(PayrollPaymentMethods.Bank, 1002)]
    [InlineData(PayrollPaymentMethods.Mobile, 1003)]
    public async Task PartiallyPaidPaymentPostsFullySettledAllocationToMethodAccount(
        string method, long settlementAccountId)
    {
        var fixture = new PostingFixture(method);

        var response = await fixture.Service.PostAsync(17, 31, 41, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        Assert.Equal(PayrollAccountingPostingTypes.SalaryPayment, response.Posting?.PostingType);
        Assert.Equal("payroll.accounting.settlement_requested",
            fixture.Audit.LastOperation?.ActionType);
        fixture.Accounting.Verify(accounting => accounting.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()),
            Times.Once);
        var input = Assert.IsType<ManualJournalDraftInput>(
            fixture.Request!.ReadFrozenPayload<ManualJournalDraftInput>());
        Assert.Equal(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), input.PostingDate);
        Assert.Equal(3, input.SourceRevision);
        Assert.Equal("BDT", input.CurrencyCode);
        Assert.Equal(500m, input.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(500m, input.Lines.Sum(line => line.CreditAmount));
        Assert.Contains(input.Lines, line =>
            line.AccountId == settlementAccountId && line.DebitAmount == 500m);
        Assert.Contains(input.Lines, line =>
            line.AccountId == 2001 && line.CreditAmount == 500m);
    }

    [Fact]
    public async Task RecoversAccountingJournalBeforeRetryingSettlementPosting()
    {
        var fixture = new PostingFixture(PayrollPaymentMethods.Cash);
        fixture.Accounting.Setup(accounting => accounting.ResolveExistingAsync(
                It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(55);

        var response = await fixture.Service.PostAsync(17, 31, 41, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        Assert.Equal(55, response.Posting?.AccountingJournalId);
        fixture.Accounting.Verify(accounting => accounting.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PayrollPaymentStatuses.Processing)]
    [InlineData(PayrollPaymentStatuses.Held)]
    [InlineData(PayrollPaymentStatuses.Failed)]
    [InlineData(PayrollPaymentStatuses.Returned)]
    [InlineData(PayrollPaymentStatuses.Cancelled)]
    public async Task UnresolvedOrTerminalPaymentStateCannotPostSettlement(string paymentStatus)
    {
        var fixture = new PostingFixture(PayrollPaymentMethods.Cash, paymentStatus);

        var response = await fixture.Service.PostAsync(17, 31, 41, CancellationToken.None);

        Assert.Equal(PayrollAccountingPostingStatuses.Failed, response.Posting?.Status);
        fixture.Accounting.Verify(accounting => accounting.PostAsync(
            It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class PostingFixture
    {
        private static readonly DateOnly EvidenceDate = new(2026, 10, 11);
        private static readonly DateTime EvidenceDateTime = new(2026, 10, 11);
        public readonly Mock<IPayrollRunAccess> Access = new(MockBehavior.Strict);
        public readonly Mock<IHrPayrollCapabilityService> Capabilities = new(MockBehavior.Strict);
        public readonly Mock<IPayrollAccountingPostingStore> Store = new(MockBehavior.Strict);
        public readonly Mock<IAccountingPostingAccountReadStore> Accounts = new(MockBehavior.Strict);
        public readonly Mock<IFrozenSystemJournalStore> Accounting = new(MockBehavior.Strict);
        public readonly Mock<IJournalEntryReadStore> Journals = new(MockBehavior.Strict);
        public readonly RecordingAudit Audit = new();
        public readonly PayrollAccountingSettlementPostingService Service;
        public FrozenSystemJournalRequest? Request { get; private set; }

        public PostingFixture(
            string method,
            string paymentStatus = PayrollPaymentStatuses.PartiallyPaid)
        {
            var source = new PayrollAccountingSettlementSource(
                1, 8, 17, 2, PayrollRunTypes.Regular, "RUN-2",
                PayrollRunScopeTypes.Location, 10, PayrollRunStatuses.Approved, 3,
                "input-hash", "approval-hash", "BDT", 2, PayrollPeriodStatuses.Approved,
                new(2026, 10, 31), 91, 42, 10, 1000m,
                17, null, paymentStatus, PayrollPaymentResolutionStatuses.None,
                1000m, 500m, paymentStatus == PayrollPaymentStatuses.Paid ? 0m : 500m,
                31, 1, method, 500m, 500m, PayrollPaymentAllocationStates.Settled,
                41, PayrollPaymentEvidenceTypes.Settlement, 500m, EvidenceDate,
                "BANK-SETTLEMENT-1", new string('A', 64),
                """[{"AllocationNo":1,"Method":"Cash","Amount":500}]""",
                51, 1, PayrollPaymentReconciliationStatuses.Reconciled,
                500m, 500m, method);
            var accountsById = new Dictionary<long, ChartAccount>
            {
                [1001] = Account(1001, "Cash", ChartAccountTypes.Asset),
                [1002] = Account(1002, "Bank", ChartAccountTypes.Asset),
                [1003] = Account(1003, "Mobile clearing", ChartAccountTypes.Asset),
                [2001] = Account(2001, "Salary payable", ChartAccountTypes.Liability)
            };

            Access.Setup(access => access.RequireBusinessId()).Returns(1);
            Access.Setup(access => access.RequireRealUserId()).Returns(101);
            Access.Setup(access => access.EnsurePermissionAsync(
                    1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Access.Setup(access => access.EnsureScopeAsync(
                    1, PayrollRunScopeTypes.Location, 10, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Capabilities.Setup(capabilities => capabilities.EnsureEnabledAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(store => store.FindSettlementSourceAsync(
                    1, 17, 31, 41, It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            Store.Setup(store => store.GetSettlementAccountMappingsAsync(
                    1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PayrollAccountingSettlementAccountMappings(
                1001, 1002, 1003, EvidenceDateTime,
                    new PayrollAccountingComponentMapping
                    {
                        Id = 7,
                        BusinessId = 1,
                        ComponentType = PayrollAccountingComponentTypes.NetPayable,
                        ComponentCode = "NET_PAYABLE",
                        AccountId = 2001,
                        IsActive = true,
                        Version = 1
                    }));
            Store.Setup(store => store.IsAccountingEnabledAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Store.Setup(store => store.LockSettlementPostingIdentityAsync(
                    1, 41, 31, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(store => store.FindSettlementPostingAsync(
                    1, 41, 31, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PayrollAccountingPosting?)null);
            Store.Setup(store => store.SavePostingAsync(
                    It.IsAny<PayrollAccountingPosting>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollAccountingPosting, CancellationToken>((posting, _) =>
                {
                    if (posting.Status == PayrollAccountingPostingStatuses.Posted)
                        SavedPosting = posting;
                })
                .Returns(Task.CompletedTask);
            Store.Setup(store => store.TryMarkPeriodPostedAsync(
                    1, 8, 101, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Accounts.Setup(accounts => accounts.GetChartAccountsForBusinessAsync(
                    1, It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long _, IReadOnlyCollection<long> ids, CancellationToken _) =>
                    ids.Select(id => accountsById[id]).ToArray());
            Accounting.Setup(accounting => accounting.PostAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), 101, It.IsAny<CancellationToken>()))
                .Callback<FrozenSystemJournalRequest, long, CancellationToken>(
                    (request, _, _) => Request = request)
                .ReturnsAsync(55);
            Accounting.Setup(accounting => accounting.ResolveExistingAsync(
                    It.IsAny<FrozenSystemJournalRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long?)null);
            Journals.Setup(journals => journals.GetByIdAsync(
                    1, 55, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostedJournal());

            Service = new PayrollAccountingSettlementPostingService(
                Access.Object, Capabilities.Object, Store.Object, Accounts.Object,
                Accounting.Object, Journals.Object, Audit,
                NullLogger<PayrollAccountingSettlementPostingService>.Instance);
        }

        private PayrollAccountingPosting? SavedPosting { get; set; }

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

        private static JournalEntryReadModel PostedJournal() =>
            new(
                55, 1, "JRN-55", AccountingJournalTypes.SystemJournal, EvidenceDateTime,
                EvidenceDateTime, EvidenceDateTime, 2026, 10, "PayrollSalaryPayment",
                "payroll-settlement:1:41:31", "BANK-SETTLEMENT-1",
                "Payroll salary settlement", null, null, null, null,
                JournalEntryStatuses.Posted, true, null, null, null, null,
                null, null, null, null, null, EvidenceDateTime, 101, EvidenceDateTime,
                101, EvidenceDateTime, 101, null, null, null, null, null, [], []);
    }

    private sealed class RecordingAudit : IRequiredAuditTransaction
    {
        public RequiredAuditOperation? LastOperation { get; private set; }

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
            return await action(cancellationToken);
        }
    }
}
