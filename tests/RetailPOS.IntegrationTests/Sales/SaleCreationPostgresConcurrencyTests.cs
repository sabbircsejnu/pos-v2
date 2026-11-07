using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Services;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Infrastructure.SupplierPayments;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public sealed class SaleCreationPostgresConcurrencyTests
{
    static SaleCreationPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentSystemJournalCreateOrGet_AfterBothLookupMisses_ResolvesToOneJournal()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var setup = harness.CreateContext())
        {
            setup.ChartAccounts.AddRange(
                new ChartAccount
                {
                    Id = 7101,
                    BusinessId = 1,
                    AccountCode = "7101",
                    AccountName = "Supplier payable",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 7102,
                    BusinessId = 1,
                    AccountCode = "7102",
                    AccountName = "Supplier advance",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                });
            await setup.SaveChangesAsync();
        }

        var lookupBarrier = new SystemJournalLookupBarrier();
        var input = new ManualJournalDraftInput(
            AccountingJournalTypes.SystemJournal,
            new DateTime(2026, 3, 15),
            new DateTime(2026, 3, 15),
            new DateTime(2026, 3, 15),
            "SupplierAdvanceUtilization",
            "PG-SYSTEM-JOURNAL-RACE",
            "PG-SYSTEM-JOURNAL-RACE",
            "Concurrent utilization",
            null,
            null,
            null,
            null,
            [
                new ManualJournalDraftLineInput(7101, null, 50m, 0m, null, null, null, null, null, null),
                new ManualJournalDraftLineInput(7102, null, 0m, 50m, null, null, null, null, null, null)
            ]);

        async Task<long> CreateOrGetAsync(long actorUserId)
        {
            await using var context = harness.CreateContext(lookupBarrier);
            var store = new ManualJournalDraftStore(
                context,
                Mock.Of<IAuditService>(audit =>
                    audit.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()) == Task.CompletedTask),
                Mock.Of<IJournalNumberAllocator>(),
                NullLogger<ManualJournalDraftStore>.Instance);
            return await store.CreateOrGetSystemDraftAsync(
                1,
                actorUserId,
                input,
                "concurrent-system-journal-test",
                CancellationToken.None);
        }

        var results = await Task.WhenAll(CreateOrGetAsync(7001), CreateOrGetAsync(7001));

        Assert.True(lookupBarrier.LookupArrivals >= 2);
        Assert.Equal(results[0], results[1]);
        await using var verify = harness.CreateContext();
        var journal = await verify.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.ReferenceType == "SupplierAdvanceUtilization"
                && entry.ReferenceId == "PG-SYSTEM-JOURNAL-RACE");
        Assert.True(journal.IsSystemGenerated);
        Assert.Equal(JournalEntryStatuses.Draft, journal.Status);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(1, await verify.JournalEntries.CountAsync(entry =>
            entry.ReferenceType == "SupplierAdvanceUtilization"
            && entry.ReferenceId == "PG-SYSTEM-JOURNAL-RACE"));
    }

    [Fact]
    public async Task ConcurrentSupplierPaymentApproval_AfterBothPreflightReads_CreatesOneAdvanceAndAudit()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var setup = harness.CreateContext())
        {
            setup.Suppliers.Add(new Supplier
            {
                Id = 7501,
                BusinessId = 1,
                Name = "Payment approval concurrency supplier"
            });
            setup.ChartAccounts.Add(new ChartAccount
            {
                Id = 7502,
                BusinessId = 1,
                AccountCode = "7502",
                AccountName = "Supplier payment cash",
                AccountType = ChartAccountTypes.Asset,
                NormalBalanceType = NormalBalanceTypes.Debit,
                IsActive = true
            });
            setup.FinancialYears.Add(new FinancialYear
            {
                Id = 7503,
                BusinessId = 1,
                YearCode = "FY-2026-PAYMENT-RACE",
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 7504,
                FinancialYearId = 7503,
                PeriodNumber = 3,
                Name = "March",
                StartDate = new DateTime(2026, 3, 1),
                EndDate = new DateTime(2026, 3, 31),
                Status = AccountingPeriodStatuses.Open
            });
            setup.JournalEntries.AddRange(
                new JournalEntry
                {
                    Id = 7505,
                    BusinessId = 1,
                    JournalNumber = "PAY-RACE-1",
                    JournalType = AccountingJournalTypes.SystemJournal,
                    TransactionDate = new DateTime(2026, 3, 15),
                    PostingDate = new DateTime(2026, 3, 15),
                    ReferenceType = "SupplierPayment",
                    ReferenceId = "7507",
                    Status = JournalEntryStatuses.Posted,
                    IsSystemGenerated = true
                },
                new JournalEntry
                {
                    Id = 7506,
                    BusinessId = 1,
                    JournalNumber = "PAY-RACE-2",
                    JournalType = AccountingJournalTypes.SystemJournal,
                    TransactionDate = new DateTime(2026, 3, 15),
                    PostingDate = new DateTime(2026, 3, 15),
                    ReferenceType = "SupplierAdvance",
                    ReferenceId = "7507:PAY-RACE",
                    Status = JournalEntryStatuses.Posted,
                    IsSystemGenerated = true
                });
            setup.SupplierPayments.Add(new SupplierPayment
            {
                Id = 7507,
                BusinessId = 1,
                OutletId = 5001,
                SupplierId = 7501,
                PaymentDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 7502,
                Amount = 50m,
                Status = "submitted",
                ReferenceNumber = "PAY-RACE",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var preflightBarrier = new SupplierPaymentApprovalPreflightBarrier();
        var paymentJournalCalls = 0;
        var advanceJournalCalls = 0;
        var auditCalls = 0;
        var poster = new Mock<ISupplierPaymentApprovalJournalPoster>(MockBehavior.Strict);
        poster.Setup(value => value.ResolvePostingAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentApprovalPostingAccounts(7508, 7509));
        poster.Setup(value => value.PostPaymentAsync(
                It.IsAny<SupplierPaymentApprovalJournalRequest>(),
                7001,
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref paymentJournalCalls))
            .ReturnsAsync(7505);
        poster.Setup(value => value.PostAdvanceAsync(
                It.IsAny<SupplierPaymentApprovalJournalRequest>(),
                7001,
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref advanceJournalCalls))
            .ReturnsAsync(7506);
        var auditWriter = new Mock<ISupplierPaymentApprovalAuditWriter>(MockBehavior.Strict);
        auditWriter.Setup(value => value.RecordApprovedAsync(
                It.IsAny<SupplierPaymentApprovalAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref auditCalls))
            .Returns(Task.CompletedTask);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.Setup(value => value.EnforceOutletAsync(5001, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)5001);

        async Task<(bool Succeeded, Exception? Error)> ApproveAsync()
        {
            await using var context = harness.CreateContext(preflightBarrier);
            var handler = new ApproveSupplierPaymentCommandHandler(
                new SupplierPaymentApprovalStore(context, poster.Object, auditWriter.Object),
                authorizer.Object);
            try
            {
                await handler.Handle(
                    new ApproveSupplierPaymentCommand(7507, 7001),
                    CancellationToken.None);
                return (true, null);
            }
            catch (Exception exception)
            {
                return (false, exception);
            }
        }

        var outcomes = await Task.WhenAll(ApproveAsync(), ApproveAsync());

        Assert.True(preflightBarrier.PreflightArrivals >= 2);
        Assert.Single(outcomes, outcome => outcome.Succeeded);
        var rejected = Assert.Single(outcomes, outcome => !outcome.Succeeded);
        Assert.IsType<InvalidOperationException>(rejected.Error);
        Assert.Equal(1, paymentJournalCalls);
        Assert.Equal(1, advanceJournalCalls);
        Assert.Equal(1, auditCalls);

        await using var verify = harness.CreateContext();
        var payment = await verify.SupplierPayments.SingleAsync(candidate => candidate.Id == 7507);
        Assert.Equal("posted", payment.Status);
        Assert.Equal(7505L, payment.JournalEntryId);
        Assert.Equal(1, await verify.SupplierAdvances.CountAsync(candidate =>
            candidate.SourcePaymentId == payment.Id));
    }

    [Fact]
    public async Task ConcurrentSupplierAdvanceApproval_AfterBothPreflightReadsPostsOnce()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var setup = harness.CreateContext())
        {
            setup.Suppliers.Add(new Supplier
            {
                Id = 5501,
                BusinessId = 1,
                Name = "Approval concurrency supplier"
            });
            setup.ChartAccounts.AddRange(
                new ChartAccount
                {
                    Id = 1001,
                    BusinessId = 1,
                    AccountCode = "1001",
                    AccountName = "Cash",
                    AccountType = ChartAccountTypes.Asset,
                    NormalBalanceType = NormalBalanceTypes.Debit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 2001,
                    BusinessId = 1,
                    AccountCode = "2001",
                    AccountName = "Supplier advance",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                });
            setup.FinancialYears.Add(new FinancialYear
            {
                Id = 6001,
                BusinessId = 1,
                YearCode = "FY-2026",
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 6101,
                FinancialYearId = 6001,
                PeriodNumber = 3,
                Name = "March",
                StartDate = new DateTime(2026, 3, 1),
                EndDate = new DateTime(2026, 3, 31),
                Status = AccountingPeriodStatuses.Open
            });
            setup.SupplierAdvances.Add(new SupplierAdvance
            {
                Id = 6201,
                BusinessId = 1,
                SupplierId = 5501,
                OutletId = 5001,
                PaymentDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 1001,
                Amount = 75m,
                RemainingAmount = 75m,
                AppliedAmount = 0m,
                Status = "submitted",
                ReferenceNumber = "ADV-PG-RACE"
            });
            await setup.SaveChangesAsync();
        }

        var preflightBarrier = new AdvanceApprovalPreflightBarrier();
        var journalPostCount = 0;
        var auditCount = 0;

        async Task<(bool Approved, Exception? Error)> ApproveAsync()
        {
            await using var context = harness.CreateContext(preflightBarrier);
            var poster = new Mock<ISupplierAdvanceApprovalJournalPoster>(MockBehavior.Strict);
            poster.Setup(value => value.ResolveAdvanceAccountIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2001);
            poster.Setup(value => value.PostAsync(
                    It.IsAny<SupplierAdvanceApprovalJournalRequest>(),
                    7001,
                    It.IsAny<CancellationToken>()))
                .Returns(async (SupplierAdvanceApprovalJournalRequest request, long actorUserId, CancellationToken cancellationToken) =>
                {
                    Interlocked.Increment(ref journalPostCount);
                    var now = DateTime.UtcNow;
                    var journal = new JournalEntry
                    {
                        BusinessId = request.BusinessId,
                        JournalNumber = "ADV-PG-RACE-1",
                        JournalType = AccountingJournalTypes.SystemJournal,
                        TransactionDate = request.PaymentDate,
                        PostingDate = request.PaymentDate,
                        BusinessDate = request.PaymentDate,
                        ReferenceType = "SupplierAdvance",
                        ReferenceId = request.AdvanceId.ToString(),
                        ReferenceNumber = request.ReferenceNumber,
                        Description = $"Supplier advance {request.AdvanceId}",
                        OutletId = request.OutletId,
                        Status = JournalEntryStatuses.Posted,
                        IsSystemGenerated = true,
                        CreatedAt = now,
                        CreatedBy = actorUserId,
                        UpdatedAt = now,
                        UpdatedBy = actorUserId,
                        PostedAt = now,
                        PostedBy = actorUserId,
                        Lines =
                        [
                            new JournalEntryLine
                            {
                                AccountId = request.AdvanceAccountId,
                                DebitAmount = request.Amount,
                                CreditAmount = 0m,
                                Description = request.Notes
                            },
                            new JournalEntryLine
                            {
                                AccountId = request.PaymentAccountId,
                                DebitAmount = 0m,
                                CreditAmount = request.Amount,
                                Description = request.Notes
                            }
                        ]
                    };
                    context.JournalEntries.Add(journal);
                    await context.SaveChangesAsync(cancellationToken);
                    return journal.Id;
                });
            var audit = new Mock<ISupplierAdvanceApprovalAuditWriter>(MockBehavior.Strict);
            audit.Setup(value => value.RecordApprovedAsync(
                    It.IsAny<SupplierAdvanceApprovalAuditRecord>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref auditCount))
                .Returns(Task.CompletedTask);
            var tenant = new Mock<ITenantAccessService>();
            tenant.Setup(value => value.RequireBusinessId()).Returns(1);
            var handler = new ApproveSupplierAdvanceCommandHandler(
                new SupplierAdvanceApprovalStore(context, poster.Object, audit.Object),
                new SupplierPaymentCreationAuthorizer(tenant.Object));

            try
            {
                await handler.Handle(
                    new ApproveSupplierAdvanceCommand(6201, 7001),
                    CancellationToken.None);
                return (true, null);
            }
            catch (Exception exception)
            {
                return (false, exception);
            }
        }

        var outcomes = await Task.WhenAll(Task.Run(ApproveAsync), Task.Run(ApproveAsync));

        Assert.Equal(2, preflightBarrier.ObservedReads);
        Assert.Single(outcomes, outcome => outcome.Approved);
        var rejected = Assert.Single(outcomes, outcome => outcome.Error is not null);
        Assert.IsType<InvalidOperationException>(rejected.Error);
        Assert.Equal("Only submitted supplier advances can be approved.", rejected.Error!.Message);
        Assert.Equal(1, journalPostCount);
        Assert.Equal(1, auditCount);

        await using var verification = harness.CreateContext();
        var advance = await verification.SupplierAdvances.SingleAsync(value => value.Id == 6201);
        Assert.Equal("posted", advance.Status);
        Assert.Equal(7001, advance.ApprovedBy);
        Assert.Equal(7001, advance.PostedBy);
        Assert.NotNull(advance.JournalEntryId);
        Assert.Equal(1, await verification.JournalEntries.CountAsync(entry =>
            entry.ReferenceType == "SupplierAdvance" && entry.ReferenceId == "6201"));
    }

    [Fact]
    public async Task ConcurrentSupplierAdvanceUtilization_AfterBothPreflightReadsCannotOverdrawAdvance()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var setup = harness.CreateContext())
        {
            setup.Suppliers.Add(new Supplier
            {
                Id = 5502,
                BusinessId = 1,
                Name = "Utilization concurrency supplier"
            });
            setup.ChartAccounts.AddRange(
                new ChartAccount
                {
                    Id = 1002,
                    BusinessId = 1,
                    AccountCode = "1002",
                    AccountName = "Cash",
                    AccountType = ChartAccountTypes.Asset,
                    NormalBalanceType = NormalBalanceTypes.Debit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 2002,
                    BusinessId = 1,
                    AccountCode = "2002",
                    AccountName = "Payable",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 2003,
                    BusinessId = 1,
                    AccountCode = "2003",
                    AccountName = "Supplier advance",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                });
            setup.FinancialYears.Add(new FinancialYear
            {
                Id = 6002,
                BusinessId = 1,
                YearCode = "FY-UTIL-2026",
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 6102,
                FinancialYearId = 6002,
                PeriodNumber = 3,
                Name = "March",
                StartDate = new DateTime(2026, 3, 1),
                EndDate = new DateTime(2026, 3, 31),
                Status = AccountingPeriodStatuses.Open
            });
            setup.JournalEntries.Add(new JournalEntry
            {
                Id = 6302,
                BusinessId = 1,
                JournalNumber = "UTIL-SOURCE-1",
                JournalType = AccountingJournalTypes.SystemJournal,
                Status = JournalEntryStatuses.Posted,
                TransactionDate = new DateTime(2026, 3, 1),
                PostingDate = new DateTime(2026, 3, 1),
                BusinessDate = new DateTime(2026, 3, 1),
                ReferenceType = "GRN",
                ReferenceId = "UTIL-SOURCE-1"
            });
            setup.SupplierPayableDocuments.Add(new SupplierPayableDocument
            {
                Id = 6402,
                BusinessId = 1,
                SupplierId = 5502,
                SourceDocumentType = "GRN",
                SourceDocumentId = 1,
                SourceReference = "UTIL-SOURCE-1",
                SourceJournalEntryId = 6302,
                OriginalPayableAmount = 100m,
                RecognitionDate = new DateTime(2026, 3, 1),
                PaymentTermsDays = 30,
                DueDate = new DateTime(2026, 3, 31),
                Status = SupplierPayableDocumentStatuses.Open
            });
            setup.SupplierAdvances.Add(new SupplierAdvance
            {
                Id = 6502,
                BusinessId = 1,
                SupplierId = 5502,
                OutletId = 5001,
                PaymentDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 1002,
                Amount = 100m,
                RemainingAmount = 100m,
                AppliedAmount = 0m,
                Status = "posted",
                ReferenceNumber = "ADV-UTIL-RACE"
            });
            await setup.SaveChangesAsync();
        }

        var preflightBarrier = new AdvanceUtilizationPreflightBarrier();
        var journalPostCount = 0;
        var auditCount = 0;

        async Task<(bool Applied, Exception? Error)> ApplyAsync(string referenceNumber)
        {
            await using var context = harness.CreateContext(preflightBarrier);
            var poster = new Mock<ISupplierAdvanceUtilizationJournalPoster>(MockBehavior.Strict);
            poster.Setup(value => value.ResolvePostingAccountsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SupplierAdvanceUtilizationPostingAccounts(2002, 2003));
            poster.Setup(value => value.PostAsync(
                    It.IsAny<SupplierAdvanceUtilizationJournalRequest>(),
                    7001,
                    It.IsAny<CancellationToken>()))
                .Returns(async (SupplierAdvanceUtilizationJournalRequest request, long actorUserId, CancellationToken cancellationToken) =>
                {
                    Interlocked.Increment(ref journalPostCount);
                    var now = DateTime.UtcNow;
                    var journal = new JournalEntry
                    {
                        BusinessId = request.BusinessId,
                        JournalNumber = $"UTIL-RACE-{request.ReferenceNumber}",
                        JournalType = AccountingJournalTypes.SystemJournal,
                        TransactionDate = request.EffectiveDate,
                        PostingDate = request.EffectiveDate,
                        BusinessDate = request.EffectiveDate,
                        ReferenceType = "SupplierAdvanceUtilization",
                        ReferenceId = request.ReferenceNumber,
                        ReferenceNumber = request.ReferenceNumber,
                        Description = request.Notes,
                        Status = JournalEntryStatuses.Posted,
                        IsSystemGenerated = true,
                        CreatedAt = now,
                        CreatedBy = actorUserId,
                        UpdatedAt = now,
                        UpdatedBy = actorUserId,
                        PostedAt = now,
                        PostedBy = actorUserId,
                        Lines =
                        [
                            new JournalEntryLine
                            {
                                AccountId = request.SupplierPayableAccountId,
                                DebitAmount = request.Amount,
                                CreditAmount = 0m
                            },
                            new JournalEntryLine
                            {
                                AccountId = request.SupplierAdvanceAccountId,
                                DebitAmount = 0m,
                                CreditAmount = request.Amount
                            }
                        ]
                    };
                    context.JournalEntries.Add(journal);
                    await context.SaveChangesAsync(cancellationToken);
                    return journal.Id;
                });
            var audit = new Mock<ISupplierAdvanceUtilizationAuditWriter>(MockBehavior.Strict);
            audit.Setup(value => value.RecordAppliedAsync(
                    It.IsAny<SupplierAdvanceUtilizationAuditRecord>(),
                    It.IsAny<CancellationToken>()))
                .Callback<SupplierAdvanceUtilizationAuditRecord, CancellationToken>(
                    (_, _) => Interlocked.Increment(ref auditCount))
                .Returns(Task.CompletedTask);
            var tenant = new Mock<ITenantAccessService>();
            tenant.Setup(value => value.RequireBusinessId()).Returns(1);
            var handler = new ApplySupplierAdvanceCommandHandler(
                new SupplierAdvanceUtilizationStore(
                    context,
                    poster.Object,
                    Mock.Of<ISupplierPaymentReversalJournalPoster>(),
                    audit.Object),
                new SupplierPaymentCreationAuthorizer(tenant.Object));

            try
            {
                await handler.Handle(
                    new ApplySupplierAdvanceCommand(
                        6502,
                        6402,
                        75m,
                        new DateTime(2026, 3, 20),
                        referenceNumber,
                        "concurrent utilization",
                        7001),
                    CancellationToken.None);
                return (true, null);
            }
            catch (Exception exception)
            {
                return (false, exception);
            }
        }

        var outcomes = await Task.WhenAll(
            Task.Run(() => ApplyAsync("UTIL-RACE-A")),
            Task.Run(() => ApplyAsync("UTIL-RACE-B")));

        Assert.Equal(2, preflightBarrier.ObservedReads);
        Assert.Single(outcomes, outcome => outcome.Applied);
        var rejected = Assert.Single(outcomes, outcome => outcome.Error is not null);
        Assert.IsType<InvalidOperationException>(rejected.Error);
        Assert.Equal("Advance application cannot exceed the available advance amount.", rejected.Error!.Message);
        Assert.Equal(1, journalPostCount);
        Assert.Equal(1, auditCount);

        await using var verification = harness.CreateContext();
        var advance = await verification.SupplierAdvances.SingleAsync(value => value.Id == 6502);
        Assert.Equal(25m, advance.RemainingAmount);
        Assert.Equal(75m, advance.AppliedAmount);
        var payable = await verification.SupplierPayableDocuments.SingleAsync(value => value.Id == 6402);
        Assert.Equal(SupplierPayableDocumentStatuses.Partial, payable.Status);
        Assert.Equal(1, await verification.SupplierAdvanceUtilizations.CountAsync(value =>
            value.SupplierAdvanceId == 6502 && value.Status == "effective"));
        Assert.Equal(1, await verification.JournalEntries.CountAsync(entry =>
            entry.ReferenceType == "SupplierAdvanceUtilization"));
    }

    [Fact]
    public async Task ConcurrentSupplierAdvanceUtilizationReversal_AfterBothPreflightReadsReversesOnce()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        const long utilizationId = 6603;
        const long originalJournalId = 6303;
        await using (var setup = harness.CreateContext())
        {
            setup.Suppliers.Add(new Supplier
            {
                Id = 5503,
                BusinessId = 1,
                Name = "Utilization reversal concurrency supplier"
            });
            setup.ChartAccounts.AddRange(
                new ChartAccount
                {
                    Id = 1003,
                    BusinessId = 1,
                    AccountCode = "1003",
                    AccountName = "Cash",
                    AccountType = ChartAccountTypes.Asset,
                    NormalBalanceType = NormalBalanceTypes.Debit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 2004,
                    BusinessId = 1,
                    AccountCode = "2004",
                    AccountName = "Payable",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                },
                new ChartAccount
                {
                    Id = 2005,
                    BusinessId = 1,
                    AccountCode = "2005",
                    AccountName = "Supplier advance",
                    AccountType = ChartAccountTypes.Liability,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    IsActive = true
                });
            setup.FinancialYears.Add(new FinancialYear
            {
                Id = 6003,
                BusinessId = 1,
                YearCode = "FY-UTIL-REV-2026",
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 6103,
                FinancialYearId = 6003,
                PeriodNumber = 3,
                Name = "March",
                StartDate = new DateTime(2026, 3, 1),
                EndDate = new DateTime(2026, 3, 31),
                Status = AccountingPeriodStatuses.Open
            });
            setup.JournalEntries.Add(new JournalEntry
            {
                Id = 6304,
                BusinessId = 1,
                JournalNumber = "UTIL-REV-SOURCE",
                JournalType = AccountingJournalTypes.SystemJournal,
                Status = JournalEntryStatuses.Posted,
                TransactionDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                ReferenceType = "GRN",
                ReferenceId = "UTIL-REV-SOURCE",
                ReferenceNumber = "UTIL-REV-SOURCE",
                IsSystemGenerated = true,
                Lines =
                [
                    new JournalEntryLine
                    {
                        AccountId = 2004,
                        DebitAmount = 75m,
                        CreditAmount = 0m
                    },
                    new JournalEntryLine
                    {
                        AccountId = 2005,
                        DebitAmount = 0m,
                        CreditAmount = 75m
                    }
                ]
            });
            setup.JournalEntries.Add(new JournalEntry
            {
                Id = originalJournalId,
                BusinessId = 1,
                JournalNumber = "UTIL-REV-ORIGINAL",
                JournalType = AccountingJournalTypes.SystemJournal,
                Status = JournalEntryStatuses.Posted,
                TransactionDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                ReferenceType = "SupplierAdvanceUtilization",
                ReferenceId = utilizationId.ToString(),
                ReferenceNumber = "UTIL-REV-ORIGINAL",
                IsSystemGenerated = true,
                Lines =
                [
                    new JournalEntryLine
                    {
                        AccountId = 2004,
                        DebitAmount = 75m,
                        CreditAmount = 0m
                    },
                    new JournalEntryLine
                    {
                        AccountId = 2005,
                        DebitAmount = 0m,
                        CreditAmount = 75m
                    }
                ]
            });
            setup.SupplierPayableDocuments.Add(new SupplierPayableDocument
            {
                Id = 6403,
                BusinessId = 1,
                SupplierId = 5503,
                SourceDocumentType = "GRN",
                SourceDocumentId = 1,
                SourceReference = "UTIL-REV-SOURCE",
                SourceJournalEntryId = 6304,
                OriginalPayableAmount = 100m,
                RecognitionDate = new DateTime(2026, 3, 1),
                PaymentTermsDays = 30,
                DueDate = new DateTime(2026, 3, 31),
                Status = SupplierPayableDocumentStatuses.Partial
            });
            setup.SupplierAdvances.Add(new SupplierAdvance
            {
                Id = 6503,
                BusinessId = 1,
                SupplierId = 5503,
                OutletId = 5001,
                PaymentDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 1003,
                Amount = 100m,
                RemainingAmount = 25m,
                AppliedAmount = 75m,
                Status = "posted",
                ReferenceNumber = "ADV-UTIL-REV-RACE"
            });
            setup.SupplierAdvanceUtilizations.Add(new SupplierAdvanceUtilization
            {
                Id = utilizationId,
                BusinessId = 1,
                SupplierAdvanceId = 6503,
                SupplierPayableDocumentId = 6403,
                Amount = 75m,
                EffectiveDate = new DateTime(2026, 3, 15),
                JournalEntryId = originalJournalId,
                Status = "effective"
            });
            await setup.SaveChangesAsync();
        }

        var preflightBarrier = new AdvanceUtilizationReversalPreflightBarrier();
        var utilizationAuditCount = 0;

        async Task<(bool Reversed, Exception? Error)> ReverseAsync(long actorUserId)
        {
            await using var context = harness.CreateContext(preflightBarrier);
            var tenant = new Mock<ITenantAccessService>();
            tenant.Setup(value => value.RequireBusinessId()).Returns(1);
            tenant.Setup(value => value.IsSuperAdmin).Returns(false);
            var journalNumbers = new Mock<IJournalNumberAllocator>(MockBehavior.Strict);
            journalNumbers.Setup(value => value.AllocateNextJournalNumberAsync(
                    1,
                    It.IsAny<long?>(),
                    It.IsAny<DateTime>(),
                    actorUserId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync($"UTIL-REV-RACE-{actorUserId}");
            var audit = new Mock<IAuditService>();
            audit.Setup(value => value.RecordAsync(
                    It.IsAny<AuditEventInput>(),
                    It.IsAny<CancellationToken>()))
                .Callback<AuditEventInput, CancellationToken>((input, _) =>
                {
                    if (input.PrimaryEntity?.Type == "SupplierAdvanceUtilization"
                        && input.ActionType == AuditActionType.Reverse)
                        Interlocked.Increment(ref utilizationAuditCount);
                })
                .Returns(Task.CompletedTask);
            var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
                new SupplierAdvanceUtilizationStore(
                    context,
                    Mock.Of<ISupplierAdvanceUtilizationJournalPoster>(),
                    new SupplierPaymentReversalJournalPoster(
                        new ManualJournalDraftStore(
                            context,
                            audit.Object,
                            journalNumbers.Object,
                            NullLogger<ManualJournalDraftStore>.Instance)),
                    new SupplierPaymentCreationAuditWriter(audit.Object)),
                new SupplierPaymentCreationAuthorizer(tenant.Object));

            try
            {
                await handler.Handle(
                    new ReverseSupplierAdvanceUtilizationCommand(
                        utilizationId,
                        new DateTime(2026, 3, 20),
                        "concurrent reversal",
                        actorUserId),
                    CancellationToken.None);
                return (true, null);
            }
            catch (Exception exception)
            {
                return (false, exception);
            }
        }

        var outcomes = await Task.WhenAll(
            Task.Run(() => ReverseAsync(7002)),
            Task.Run(() => ReverseAsync(7003)));

        Assert.Equal(2, preflightBarrier.ObservedReads);
        Assert.Single(outcomes, outcome => outcome.Reversed);
        Assert.IsType<InvalidOperationException>(
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error);

        await using var verification = harness.CreateContext();
        var utilization = await verification.SupplierAdvanceUtilizations.SingleAsync(
            value => value.Id == utilizationId);
        Assert.Equal("reversed", utilization.Status);
        Assert.Equal(1, utilizationAuditCount);
        var reversalRows = await verification.SupplierAdvanceUtilizations
            .Where(value => value.ReversalOfUtilizationId == utilizationId)
            .ToListAsync();
        Assert.Single(reversalRows);
        var advance = await verification.SupplierAdvances.SingleAsync(value => value.Id == 6503);
        Assert.Equal(100m, advance.RemainingAmount);
        Assert.Equal(0m, advance.AppliedAmount);
        var payable = await verification.SupplierPayableDocuments.SingleAsync(value => value.Id == 6403);
        Assert.Equal(SupplierPayableDocumentStatuses.Open, payable.Status);
        Assert.Equal(1, await verification.JournalEntries.CountAsync(
            value => value.ReversalOfJournalEntryId == originalJournalId));
        var originalJournal = await verification.JournalEntries.SingleAsync(
            value => value.Id == originalJournalId);
        Assert.Equal(JournalEntryStatuses.Reversed, originalJournal.Status);
        Assert.NotNull(originalJournal.ReversedByJournalEntryId);
    }

    [Fact]
    public async Task ConcurrentCashSessionOpen_AfterBothPreflightReadsPersistsOnlyOneActiveSession()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var setup = harness.CreateContext())
        {
            var existing = await setup.CashSessions
                .Where(session => session.OutletId == 5001
                    && session.TerminalId == 8001
                    && session.Status == "open")
                .ToListAsync();
            setup.CashSessions.RemoveRange(existing);
            await setup.SaveChangesAsync();
        }

        var preflightBarrier = new PostgresHarness.CashSessionOpenPreflightBarrier();
        var now = DateTime.UtcNow;

        async Task<(CashSessionReadModel? Session, Exception? Error)> OpenAsync()
        {
            try
            {
                await using var context = harness.CreateContext(preflightBarrier);
                var outletAuthorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
                outletAuthorizer
                    .Setup(authorizer => authorizer.AuthorizeWriteAsync(5001, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(5001);
                var audit = new Mock<ICashSessionOpenAuditWriter>();
                audit.Setup(writer => writer.RecordOpenedAsync(
                        It.IsAny<CashSessionSnapshot>(),
                        125m,
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var publisher = new Mock<ICashSessionOpenedPublisher>();
                publisher.Setup(eventPublisher => eventPublisher.PublishOpenedAsync(
                        It.IsAny<CashSessionSnapshot>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var settings = new Mock<RetailPOS.Application.Settings.Abstractions.IBusinessPosSettingsStore>();
                settings.Setup(readSettings => readSettings.GetAsync(1, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RetailPOS.Application.Settings.Abstractions.BusinessPosSettingsSnapshot(
                        1, 20, "Asia/Dhaka", true, 10m, "VAT", false, 0m, 1, null, null, true));
                var handler = new OpenCashSessionCommandHandler(
                    new CashSessionStore(context),
                    outletAuthorizer.Object,
                    settings.Object,
                    TestCompanySettings.Provider(),
                    audit.Object,
                    publisher.Object,
                    new RetailPOS.IntegrationTests.Infrastructure.NullCashSessionEvents(),
                    TimeProvider.System);

                return (await handler.Handle(new OpenCashSessionCommand(
                    5001,
                    8001,
                    125m,
                    null,
                    7001), CancellationToken.None), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var results = await Task.WhenAll(Task.Run(OpenAsync), Task.Run(OpenAsync));

        // Each open reads the terminals/occupancy twice (context + fast-path check); the unique index decides the race.
        Assert.True(preflightBarrier.ObservedReads >= 2);
        var successes = results.Where(result => result.Session is not null).ToArray();
        var failures = results.Where(result => result.Error is not null).ToArray();
        Assert.Single(successes);
        var concurrencyError = Assert.IsType<RetailPOS.Application.Pos.CashSessionException>(Assert.Single(failures).Error);
        Assert.Equal(RetailPOS.Application.Pos.CashSessionErrorCodes.AlreadyOpen, concurrencyError.Code);
        await using var verification = harness.CreateContext();
        var activeCount = await verification.CashSessions.CountAsync(session =>
            session.OutletId == 5001 && session.TerminalId == 8001 && session.Status == "open");
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task ConcurrentSameKeyAfterBothInitialMisses_CommitsOnlyOneSaleAndStockChain()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        var barrier = new IdempotencyLookupBarrier();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(long? SaleId, Exception? Error)> CreateAfterStartAsync()
        {
            await start.Task;
            try
            {
                await using var context = harness.CreateContext();
                var cache = new Mock<IPosCacheService>();
                cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Returns(Task.CompletedTask);
                var events = new Mock<ISaleEventPublisher>();
                events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()))
                    .Returns(Task.CompletedTask);
                var service = new CreateSaleCommandHandler(
                    new IdempotencyLookupBarrierStore(
                        new SaleCreationStore(context, new SaleReadStore(context, TestCompanySettings.Provider())),
                        barrier),
                    harness.SaleCashSessionValidator.Object,
                    harness.InvoiceNumberAllocator.Object,
                    SaleCreationCommandTestFactory.FinancialPostings(context),
                    new SaleCreationAuditWriter(Mock.Of<IAuditService>()),
                    new SaleStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                        context,
                        NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
                    new SaleStockCacheInvalidator(cache.Object),
                    new SaleCompletedPublisher(events.Object),
                    new SaleCreationLogger(NullLogger<SaleCreationLogger>.Instance),
                    SaleCreationCommandTestFactory.PriceCalculator(context),
                    SaleCreationCommandTestFactory.TaxPolicy());

                var created = await service.Handle(new CreateSaleCommand(
                    5001,
                    8001,
                    null,
                    [new CreateSaleLine(3201, 2, 100m, 0m, null, null)],
                    0m,
                    0m,
                    "cash",
                    [],
                    7001,
                    null,
                    DateTime.UtcNow,
                    "pg-concurrent-sale",
                    200m), CancellationToken.None);
                return (created.Id, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var attempts = new[] { CreateAfterStartAsync(), CreateAfterStartAsync() };
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);
        var failures = outcomes.Where(outcome => outcome.Error is not null).ToArray();

        Assert.Equal(2, barrier.MissedLookups);
        Assert.True(
            failures.Length == 0,
            string.Join(" | ", failures
                .Select(outcome =>
                    $"{outcome.Error!.GetType().Name}: {outcome.Error.GetBaseException().Message}")));

        // The race loser hits the unique index, rolls back and returns the winner's sale: both callers see one sale.
        await using var verify = harness.CreateContext();
        var sale = await verify.Sales.SingleAsync(x => x.IdempotencyKey == "pg-concurrent-sale");
        Assert.All(outcomes, outcome => Assert.Equal(sale.Id, outcome.SaleId));
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Single(await verify.SaleItems.ToListAsync());
        Assert.Single(await verify.SalePayments.ToListAsync());
        Assert.Single(await verify.StockLedgers.ToListAsync());
        Assert.Equal(48, (await verify.Inventories.SingleAsync()).Quantity);

        var idempotencyIndex = verify.Model.FindEntityType(typeof(Sale))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Sale.OutletId), nameof(Sale.IdempotencyKey)]));
        Assert.True(idempotencyIndex.IsUnique);
        Assert.Contains("idempotency_key IS NOT NULL", idempotencyIndex.GetFilter());
        var databaseIndex = await verify.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes " +
                "WHERE tablename = 'sales' AND indexdef ILIKE '%UNIQUE INDEX%' " +
                "AND indexdef ILIKE '%outlet_id%' AND indexdef ILIKE '%idempotency_key%'")
            .SingleAsync();
        Assert.Contains("WHERE (idempotency_key IS NOT NULL)", databaseIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(outcomes, outcome => outcome.SaleId == sale.Id || outcome.Error is not null);
    }

    [Fact]
    public async Task ConcurrentExchangesOfTheSameOriginalSale_CommitOnlyOneReplacement()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var seed = harness.CreateContext())
        {
            seed.Sales.Add(new Sale
            {
                Id = 7001,
                SaleNumber = "ORIG-7001",
                OutletId = 5001,
                CashierId = 7001,
                Status = "completed",
                TotalAmount = 200m
            });
            await seed.SaveChangesAsync();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(long? SaleId, Exception? Error)> ExchangeAsync(string key)
        {
            await start.Task;
            try
            {
                await using var context = harness.CreateContext();
                var cache = new Mock<IPosCacheService>();
                cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
                    .Returns(Task.CompletedTask);
                var events = new Mock<ISaleEventPublisher>();
                events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()))
                    .Returns(Task.CompletedTask);
                var service = SaleCreationCommandTestFactory.Create(
                    context,
                    harness.SaleCashSessionValidator.Object,
                    harness.InvoiceNumberAllocator.Object,
                    Mock.Of<IAuditService>(),
                    new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                        context,
                        NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
                    cache.Object,
                    events.Object);

                var created = await service.Handle(new CreateSaleCommand(
                    5001,
                    8001,
                    null,
                    [new CreateSaleLine(3201, 2, 100m, 0m, null, null)],
                    0m,
                    0m,
                    "cash",
                    [],
                    7001,
                    7001,
                    DateTime.UtcNow,
                    key,
                    200m), CancellationToken.None);
                return (created.Id, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var attempts = new[] { ExchangeAsync("exchange-a"), ExchangeAsync("exchange-b") };
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        var winner = Assert.Single(outcomes, outcome => outcome.SaleId.HasValue);
        var loser = Assert.Single(outcomes, outcome => outcome.Error is not null);
        Assert.IsType<SaleCheckoutValidationException>(loser.Error);

        await using var verify = harness.CreateContext();
        Assert.Equal(winner.SaleId, (await verify.Sales.SingleAsync(sale => sale.Id == 7001)).ReplacementSaleId);
        Assert.Equal(1, await verify.Sales.CountAsync(sale => sale.OriginalSaleId == 7001));
        Assert.Equal(48, (await verify.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task MigrationBuiltSchema_PersistsPaymentEvidenceAndTheIdempotencyFingerprint()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var context = harness.CreateContext();
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var events = new Mock<ISaleEventPublisher>();
        events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>())).Returns(Task.CompletedTask);
        var service = SaleCreationCommandTestFactory.Create(
            context,
            harness.SaleCashSessionValidator.Object,
            harness.InvoiceNumberAllocator.Object,
            Mock.Of<IAuditService>(),
            new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                context,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
            cache.Object,
            events.Object);

        var created = await service.Handle(new CreateSaleCommand(
            5001,
            8001,
            null,
            [new CreateSaleLine(3201, 2, 100m, 0m, null, null)],
            0m,
            0m,
            "cash",
            [],
            7001,
            null,
            DateTime.UtcNow,
            "pg-evidence",
            250m), CancellationToken.None);

        await using var verify = harness.CreateContext();
        var payment = await verify.SalePayments.SingleAsync(item => item.SaleId == created.Id);
        Assert.Equal(200m, payment.Amount);
        Assert.Equal(250m, payment.Tendered);
        Assert.Equal(50m, payment.ChangeGiven);
        Assert.Equal(200m, payment.NetCashRetained);
        Assert.Equal(64, (await verify.Sales.SingleAsync(sale => sale.Id == created.Id)).IdempotencyFingerprint?.Length);

        var replayConflict = await Assert.ThrowsAsync<SaleCheckoutConflictException>(() => service.Handle(
            new CreateSaleCommand(
                5001, 8001, null, [new CreateSaleLine(3201, 1, 100m, 0m, null, null)], 0m, 0m, "cash", [],
                7001, null, DateTime.UtcNow, "pg-evidence", 100m),
            CancellationToken.None));
        Assert.Equal(SaleCheckoutErrorCodes.IdempotencyPayloadMismatch, replayConflict.Code);
    }

    [Fact]
    public async Task ConcurrentVoidAfterBothEligibleReads_CommitsOneCompleteReversal()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var seed = harness.CreateContext())
        {
            var loyaltyCustomer = new Customer
            {
                Id = 6002,
                BusinessId = 1,
                Name = "Loyalty customer",
                CustomerCode = "LOYALTY-PG-VOID",
                IsSystem = false,
                IsActive = true,
                LoyaltyPoints = 500
            };
            var saleItem = new SaleItem
            {
                VariantId = 3201,
                Quantity = 2,
                UnitPrice = 100m,
                Subtotal = 200m,
                DiscountAmount = 0m
            };
            saleItem.CaptureHistoricalUnitCost(70m);
            var inventory = await seed.Inventories.SingleAsync(item => item.VariantId == 3201);
            inventory.Quantity = 48;
            seed.Customers.Add(loyaltyCustomer);
            seed.Sales.Add(new Sale
            {
                Id = 9901,
                SaleNumber = "PG-VOID-9901",
                OutletId = 5001,
                CustomerId = loyaltyCustomer.Id,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                SaleDateTime = DateTime.UtcNow,
                BusinessDate = DateTime.UtcNow.Date,
                Status = "completed",
                TotalAmount = 123m,
                PaymentMethod = "cash",
                Items = [saleItem],
                Payments = [new SalePayment { Method = "cash", Amount = 123m }]
            });
            await seed.SaveChangesAsync();
        }

        var eligibleReadBarrier = new EligibleVoidReadBarrier();
        var initialStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        accounting.Setup(service => service.AppendReversalAsync(
                It.IsAny<Sale>(),
                It.IsAny<IReadOnlyList<SaleRefundLine>>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaleFinancialPosting());
        var audits = new System.Collections.Concurrent.ConcurrentBag<AuditEventInput>();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => audits.Add(input))
            .Returns(Task.CompletedTask);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(service => service.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);

        async Task<Exception?> VoidAfterBothEligibleReadsAsync(long actorUserId)
        {
            await initialStart.Task;
            try
            {
                await using var context = harness.CreateContext();
                var handler = new VoidSaleCommandHandler(
                    new EligibleVoidReadBarrierStore(new SaleVoidStore(context), eligibleReadBarrier),
                    new SaleReadStore(context, TestCompanySettings.Provider()),
                    CreateOutletAuthorizer(),
                    accounting.Object,
                    new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                        context,
                        NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
                    new SaleVoidAuditWriter(audit.Object),
                    new SaleVoidCacheInvalidator(cache.Object),
                    new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
                    new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());
                await handler.Handle(
                    new VoidSaleCommand(9901, "concurrent void", actorUserId),
                    CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var requests = new[]
        {
            VoidAfterBothEligibleReadsAsync(7001),
            VoidAfterBothEligibleReadsAsync(7001)
        };
        initialStart.SetResult();
        var outcomes = await Task.WhenAll(requests);

        Assert.Equal(2, eligibleReadBarrier.EligibleReads);
        Assert.Single(outcomes, exception => exception is null);
        var losingRequest = Assert.IsType<InvalidOperationException>(
            Assert.Single(outcomes, exception => exception is not null));
        Assert.Equal("Sale is already voided", losingRequest.Message);
        accounting.Verify(service => service.AppendReversalAsync(
            It.Is<Sale>(sale => sale.Id == 9901),
            It.Is<IReadOnlyList<SaleRefundLine>>(lines =>
                lines.Count == 1 &&
                lines[0].SaleItemId > 0 &&
                lines[0].VariantId == 3201 &&
                lines[0].Quantity == 2),
            It.IsAny<string>(),
            7001,
            It.IsAny<CancellationToken>(),
            It.IsAny<SaleRefundPostingContext?>()), Times.Once);
        Assert.Single(audits);
        Assert.Null(audits.Single().Metadata?["journalEntryId"]);
        cache.Verify(service => service.InvalidateStockAsync(3201, 5001), Times.Once);

        await using var verify = harness.CreateContext();
        var voidedSale = await verify.Sales.SingleAsync(sale => sale.Id == 9901);
        Assert.Equal("voided", voidedSale.Status);
        Assert.Single(await verify.SaleVoids.Where(history => history.SaleId == 9901).ToListAsync());
        var ledger = await verify.StockLedgers
            .Where(entry => entry.ReferenceId == 9901 && entry.TransactionType == StockLedgerTransactionType.Return)
            .ToListAsync();
        Assert.Single(ledger);
        Assert.Equal(2, ledger[0].QtyIn);
        Assert.Equal(70m, ledger[0].UnitCost);
        Assert.Equal(50, (await verify.Inventories.SingleAsync(item => item.VariantId == 3201)).Quantity);
        // Phase 2.1: voiding no longer touches the legacy integer loyalty balance.
        Assert.Equal(500, (await verify.Customers.SingleAsync(customer => customer.Id == 6002)).LoyaltyPoints);
    }

    [Fact]
    public async Task ConcurrentRefundAfterBothEligibleReads_CommitsOneCompleteRefund()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using (var seed = harness.CreateContext())
        {
            var inventory = await seed.Inventories.SingleAsync(item => item.VariantId == 3201);
            inventory.Quantity = 48;
            var item = new SaleItem
            {
                VariantId = 3201,
                Quantity = 2,
                UnitPrice = 100m,
                Subtotal = 200m,
                DiscountAmount = 0m
            };
            item.CaptureHistoricalUnitCost(70m);
            seed.Sales.Add(new Sale
            {
                Id = 9910,
                SaleNumber = "PG-REFUND-9910",
                OutletId = 5001,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                SaleDateTime = DateTime.UtcNow,
                BusinessDate = DateTime.UtcNow.Date,
                Status = "completed",
                TotalAmount = 200m,
                PaymentMethod = "cash",
                Items = [item],
                Payments = [new SalePayment { Method = "cash", Amount = 200m }]
            });
            await seed.SaveChangesAsync();
        }

        var eligibleReadBarrier = new EligibleRefundReadBarrier();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        // A refund reads its amounts back from the intent, so the double returns a real one, as the writer does.
        accounting.Setup(service => service.AppendReversalAsync(
                It.IsAny<Sale>(),
                It.IsAny<IReadOnlyList<SaleRefundLine>>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<SaleRefundPostingContext?>()))
            .ReturnsAsync((Sale sale, IReadOnlyList<SaleRefundLine> lines, string type, long actor, CancellationToken _, SaleRefundPostingContext? refund) =>
                SaleFinancialIntentFactory.ForReversal(sale, sale.BusinessId ?? 1, lines, type, actor, DateTime.UtcNow, refund: refund));
        var auditEvents = new System.Collections.Concurrent.ConcurrentBag<AuditEventInput>();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(service => service.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);

        async Task<Exception?> RefundAfterBothEligibleReadsAsync()
        {
            await start.Task;
            try
            {
                await using var context = harness.CreateContext();
                var handler = new RefundSaleCommandHandler(
                    new EligibleRefundReadBarrierStore(
                        new SaleVoidStore(context),
                        eligibleReadBarrier),
                    new SaleReadStore(context, TestCompanySettings.Provider()),
                    CreateOutletAuthorizer(),
                    accounting.Object,
                    new SaleRefundStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                        context,
                        NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
                    new SaleRefundAuditWriter(audit.Object),
                    new SaleRefundCacheInvalidator(cache.Object),
                    Mock.Of<ISaleRefundLogger>(),
                    new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());
                await handler.Handle(
                    new RefundSaleCommand(
                        9910,
                        [new RefundSaleLine(3201, 1)],
                        "concurrent refund",
                        7001),
                    CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var attempts = new[] { RefundAfterBothEligibleReadsAsync(), RefundAfterBothEligibleReadsAsync() };
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(2, eligibleReadBarrier.EligibleReads);
        Assert.Single(outcomes, exception => exception is null);
        var losingRequest = Assert.IsType<InvalidOperationException>(
            Assert.Single(outcomes, exception => exception is not null));
        Assert.Equal("Sale is already refunded", losingRequest.Message);
        accounting.Verify(service => service.AppendReversalAsync(
            It.Is<Sale>(sale => sale.Id == 9910),
            It.Is<IReadOnlyList<SaleRefundLine>>(lines =>
                lines.Count == 1 &&
                lines[0].SaleItemId > 0 &&
                lines[0].VariantId == 3201 &&
                lines[0].Quantity == 1),
            It.IsAny<string>(),
            7001,
            It.IsAny<CancellationToken>(),
            It.IsAny<SaleRefundPostingContext?>()), Times.Once);
        Assert.Single(auditEvents);
        Assert.Null(auditEvents.Single().Metadata?["journalEntryId"]);
        cache.Verify(service => service.InvalidateStockAsync(3201, 5001), Times.Once);

        await using var verify = harness.CreateContext();
        // One of two units was returned, so the sale stays partly refundable (I02).
        Assert.Equal("partially_refunded", await verify.Sales.Where(sale => sale.Id == 9910)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(49, (await verify.Inventories.SingleAsync(item => item.VariantId == 3201)).Quantity);
        var ledger = await verify.StockLedgers
            .Where(entry => entry.ReferenceId == 9910 &&
                            entry.TransactionType == StockLedgerTransactionType.Return)
            .ToListAsync();
        Assert.Single(ledger);
        Assert.Equal(1, ledger[0].QtyIn);
        Assert.Equal(70m, ledger[0].UnitCost);
    }

    private sealed class EligibleVoidReadBarrier
    {
        private readonly TaskCompletionSource _bothEligible =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _eligibleReads;

        public int EligibleReads => Volatile.Read(ref _eligibleReads);

        public async Task WaitForBothEligibleReadsAsync()
        {
            if (Interlocked.Increment(ref _eligibleReads) == 2)
                _bothEligible.TrySetResult();
            await _bothEligible.Task;
        }

    }

    private sealed class EligibleVoidReadBarrierStore(
        ISaleVoidStore inner,
        EligibleVoidReadBarrier barrier) : ISaleVoidStore
    {
        public async Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken)
        {
            var sale = await inner.FindSaleForUpdateAsync(saleId, cancellationToken);
            if (sale?.Status == "completed")
                await barrier.WaitForBothEligibleReadsAsync();
            return sale;
        }

        public Task<ISaleVoidTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            inner.BeginTransactionAsync(cancellationToken);
        public Task<string?> FindCurrentSaleStatusAsync(long saleId, CancellationToken cancellationToken) =>
            inner.FindCurrentSaleStatusAsync(saleId, cancellationToken);
        public Task<bool> TryMarkCompletedSaleVoidedAsync(long saleId, CancellationToken cancellationToken) =>
            inner.TryMarkCompletedSaleVoidedAsync(saleId, cancellationToken);
        public Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
            long variantId, long outletId, CancellationToken cancellationToken) =>
            inner.FindOutletInventoryAsync(variantId, outletId, cancellationToken);
        public Task<Customer?> FindCustomerForUpdateAsync(long customerId, CancellationToken cancellationToken) =>
            inner.FindCustomerForUpdateAsync(customerId, cancellationToken);
        public Task AddSaleVoidAsync(SaleVoid saleVoid, CancellationToken cancellationToken) =>
            inner.AddSaleVoidAsync(saleVoid, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class EligibleRefundReadBarrier
    {
        private readonly TaskCompletionSource _bothEligible =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _eligibleReads;

        public int EligibleReads => Volatile.Read(ref _eligibleReads);

        public async Task WaitForBothEligibleReadsAsync()
        {
            if (Interlocked.Increment(ref _eligibleReads) == 2)
                _bothEligible.TrySetResult();
            await _bothEligible.Task;
        }
    }

    private sealed class EligibleRefundReadBarrierStore(
        ISaleRefundStore inner,
        EligibleRefundReadBarrier barrier) : ISaleRefundStore
    {
        public Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken) =>
            inner.FindSaleForUpdateAsync(saleId, cancellationToken);

        // Both refunds arrive together at the lock, which now comes before the read, and only one holds it at a time.
        public async Task LockSaleAsync(long saleId, CancellationToken cancellationToken)
        {
            await barrier.WaitForBothEligibleReadsAsync();
            await inner.LockSaleAsync(saleId, cancellationToken);
        }

        public Task AddRefundAsync(RetailPOS.Core.Entities.Uom.SaleRefund refund, CancellationToken cancellationToken) =>
            inner.AddRefundAsync(refund, cancellationToken);

        public Task<ISaleVoidTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            inner.BeginTransactionAsync(cancellationToken);
        public Task<string?> FindCurrentSaleStatusAsync(long saleId, CancellationToken cancellationToken) =>
            inner.FindCurrentSaleStatusAsync(saleId, cancellationToken);
        public Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
            long variantId,
            long outletId,
            CancellationToken cancellationToken) =>
            inner.FindOutletInventoryAsync(variantId, outletId, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private static ISaleMutationOutletAuthorizer CreateOutletAuthorizer()
    {
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return authorizer.Object;
    }

    private sealed class IdempotencyLookupBarrier
    {
        private readonly TaskCompletionSource _bothMissed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _missedLookups;

        public int MissedLookups => Volatile.Read(ref _missedLookups);

        public async Task WaitForBothMissesAsync()
        {
            if (Interlocked.Increment(ref _missedLookups) == 2)
                _bothMissed.TrySetResult();
            await _bothMissed.Task;
        }
    }

    private sealed class IdempotencyLookupBarrierStore(
        ISaleCreationStore inner,
        IdempotencyLookupBarrier barrier) : ISaleCreationStore
    {
        public async Task<SaleIdempotencyMatch?> FindByIdempotencyKeyAsync(
            long outletId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var existing = await inner.FindByIdempotencyKeyAsync(outletId, idempotencyKey, cancellationToken);
            if (existing is null)
                await barrier.WaitForBothMissesAsync();
            return existing;
        }

        public bool IsIdempotencyKeyViolation(Exception exception) => inner.IsIdempotencyKeyViolation(exception);
        public Task<IReadOnlyList<long>> FindUnsellableVariantIdsAsync(
            IReadOnlyCollection<long> variantIds, long businessId, CancellationToken cancellationToken) =>
            inner.FindUnsellableVariantIdsAsync(variantIds, businessId, cancellationToken);
        public Task<ExchangeOriginalSale?> FindExchangeOriginalSaleForUpdateAsync(
            long saleId, CancellationToken cancellationToken) =>
            inner.FindExchangeOriginalSaleForUpdateAsync(saleId, cancellationToken);
        public Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken) =>
            inner.GetOutletBusinessIdAsync(outletId, cancellationToken);
        public Task<bool> IsEligibleSalespersonAsync(long userId, long businessId, long outletId, CancellationToken cancellationToken) =>
            inner.IsEligibleSalespersonAsync(userId, businessId, outletId, cancellationToken);

        public Task<ISaleCreationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            inner.BeginTransactionAsync(cancellationToken);
        public Task<Customer?> FindCustomerAsync(long customerId, long businessId, CancellationToken cancellationToken) =>
            inner.FindCustomerAsync(customerId, businessId, cancellationToken);
        public Task<Customer?> FindWalkInCustomerAsync(long businessId, CancellationToken cancellationToken) =>
            inner.FindWalkInCustomerAsync(businessId, cancellationToken);
        public Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken) =>
            inner.AddCustomerAsync(customer, cancellationToken);
        public Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
            long variantId, long outletId, CancellationToken cancellationToken) =>
            inner.FindOutletInventoryAsync(variantId, outletId, cancellationToken);
        public Task<long?> ResolveActiveTerminalIdAsync(
            long outletId, long? requestedTerminalId, CancellationToken cancellationToken) =>
            inner.ResolveActiveTerminalIdAsync(outletId, requestedTerminalId, cancellationToken);
        public Task<decimal> ResolveHistoricalUnitCostSnapshotAsync(long variantId, CancellationToken cancellationToken) =>
            inner.ResolveHistoricalUnitCostSnapshotAsync(variantId, cancellationToken);
        public Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken) =>
            inner.FindSaleForUpdateAsync(saleId, cancellationToken);
        public Task<Customer?> FindCustomerForUpdateAsync(long customerId, CancellationToken cancellationToken) =>
            inner.FindCustomerForUpdateAsync(customerId, cancellationToken);
        public Task AddSaleAsync(Sale sale, CancellationToken cancellationToken) =>
            inner.AddSaleAsync(sale, cancellationToken);
        public Task<SaleReadModel?> FindCreatedSaleAsync(long saleId, CancellationToken cancellationToken) =>
            inner.FindCreatedSaleAsync(saleId, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);
        public Task SaveFinalChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveFinalChangesAsync(cancellationToken);
    }

    private sealed class AdvanceApprovalPreflightBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReadsExecuted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<Guid, byte> _observedContexts = new();
        private int _observedReads;

        public int ObservedReads => Volatile.Read(ref _observedReads);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("supplier_advances", StringComparison.OrdinalIgnoreCase)
                && eventData.Context is { } context
                && _observedContexts.TryAdd(context.ContextId.InstanceId, 0))
            {
                if (Interlocked.Increment(ref _observedReads) == 2)
                    _bothReadsExecuted.TrySetResult();

                await _bothReadsExecuted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class AdvanceUtilizationPreflightBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReadsExecuted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<Guid, byte> _observedContexts = new();
        private int _observedReads;

        public int ObservedReads => Volatile.Read(ref _observedReads);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("supplier_advances", StringComparison.OrdinalIgnoreCase)
                && eventData.Context is { } context
                && _observedContexts.TryAdd(context.ContextId.InstanceId, 0))
            {
                if (Interlocked.Increment(ref _observedReads) == 2)
                    _bothReadsExecuted.TrySetResult();

                await _bothReadsExecuted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class AdvanceUtilizationReversalPreflightBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReadsExecuted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<Guid, byte> _observedContexts = new();
        private int _observedReads;

        public int ObservedReads => Volatile.Read(ref _observedReads);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("supplier_advance_utilizations", StringComparison.OrdinalIgnoreCase)
                && eventData.Context is { } context
                && _observedContexts.TryAdd(context.ContextId.InstanceId, 0))
            {
                if (Interlocked.Increment(ref _observedReads) == 2)
                    _bothReadsExecuted.TrySetResult();

                await _bothReadsExecuted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class SystemJournalLookupBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothLookupsReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _lookupArrivals;

        public int LookupArrivals => Volatile.Read(ref _lookupArrivals);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("journal_entries", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("reference_id", StringComparison.OrdinalIgnoreCase))
            {
                var arrival = Interlocked.Increment(ref _lookupArrivals);
                if (arrival <= 2)
                {
                    if (arrival == 2)
                        _bothLookupsReached.TrySetResult();
                    await _bothLookupsReached.Task.WaitAsync(cancellationToken);
                }
            }

            return result;
        }
    }

    private sealed class SupplierPaymentApprovalPreflightBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothPreflightsReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _preflightArrivals;

        public int PreflightArrivals => Volatile.Read(ref _preflightArrivals);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("supplier_payments", StringComparison.OrdinalIgnoreCase))
            {
                var arrival = Interlocked.Increment(ref _preflightArrivals);
                if (arrival <= 2)
                {
                    if (arrival == 2)
                        _bothPreflightsReached.TrySetResult();
                    await _bothPreflightsReached.Task.WaitAsync(cancellationToken);
                }
            }

            return result;
        }
    }

    private sealed class PostgresHarness : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        private readonly string _connectionString;
        private readonly Customer _walkIn;
        private readonly CashSession _cashSession;

        private PostgresHarness(
            PostgresTestDatabase database,
            Customer walkIn,
            CashSession cashSession)
        {
            _database = database;
            _connectionString = database.ConnectionString;
            _walkIn = walkIn;
            _cashSession = cashSession;
        }

        public Mock<ISaleCashSessionValidator> SaleCashSessionValidator
        {
            get
            {
                var service = new Mock<ISaleCashSessionValidator>();
                service.Setup(x => x.ValidateActiveForSaleAsync(
                        5001,
                        8001,
                        7001,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_cashSession);
                return service;
            }
        }

        public Mock<ISaleInvoiceNumberAllocator> InvoiceNumberAllocator
        {
            get
            {
                var service = new Mock<ISaleInvoiceNumberAllocator>();
                service.Setup(x => x.AllocateAsync(
                        It.IsAny<long>(),
                        It.IsAny<long>(),
                        It.IsAny<DateTime>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => TestInvoices.Issue($"PG-{Guid.NewGuid():N}"[..30]));
                return service;
            }
        }

        public RetailPOSDbContext CreateContext(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(_connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
            return new RetailPOSDbContext(options.Options);
        }

        public static async Task<PostgresHarness> CreateAsync()
        {
            var baseConnectionString = InfrastructureTestSettings.RequireIsolatedPostgres();

            var database = await PostgresTestDatabase.CreateAsync(
                baseConnectionString,
                PostgresTestDatabasePrefix.SaleCreation);

            var walkIn = new Customer
            {
                Id = 6001,
                BusinessId = 1,
                Name = Customer.WalkInCustomerName,
                CustomerCode = Customer.WalkInCustomerCode,
                IsSystem = true,
                IsActive = true
            };
            var cashSession = new CashSession
            {
                Id = 8101,
                BusinessId = 1,
                OutletId = 5001,
                TerminalId = 8001,
                BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow,
                OpeningCashAmount = 1000m,
                Status = "open",
                OpenedByUserId = 7001
            };

            try
            {
                var harness = new PostgresHarness(database, walkIn, cashSession);
                await using (var db = harness.CreateContext())
                {
                    // Migration-built schema so triggers, partial indexes and raw-SQL constraints are part of the test.
                    await db.Database.MigrateAsync();
                    db.Businesses.Add(new Business { Id = 1, Name = "Sale concurrency business", IsActive = true });
                    db.Outlets.Add(new Outlet { Id = 5001, BusinessId = 1, Name = "Main outlet", Address = "Dhaka" });
                    db.Users.Add(new User
                    {
                        Id = 7001,
                        BusinessId = 1,
                        Name = "Cashier",
                        Email = "sale-concurrency@example.test",
                        PasswordHash = "test",
                        OutletId = 5001,
                        IsActive = true
                    });
                    db.Customers.Add(walkIn);
                    db.Categories.Add(new Category { Id = 3001, BusinessId = 1, Name = "General" });
                    db.Products.Add(new Product
                    {
                        Id = 3101,
                        BusinessId = 1,
                        Name = "Test product",
                        ProductCode = "P-3101",
                        CategoryId = 3001,
                        BasePrice = 100m,
                        CostPrice = 70m,
                        Status = ProductStatus.Active,
                        HasVariants = true
                    });
                    db.ProductVariants.Add(new ProductVariant
                    {
                        Id = 3201,
                        BusinessId = 1,
                        ProductId = 3101,
                        Name = "Default",
                        Sku = "SKU-3201",
                        Barcode = "BC-3201",
                        Attributes = "{}"
                    });
                    db.PosTerminals.Add(new PosTerminal
                    {
                        Id = 8001,
                        OutletId = 5001,
                        Name = "Counter",
                        Code = "T-1",
                        IsActive = true,
                        IsDefault = true
                    });
                    db.CashSessions.Add(cashSession);
                    db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
                    {
                        Id = 9001,
                        VariantId = 3201,
                        LocationType = "outlet",
                        LocationId = 5001,
                        Quantity = 50,
                        LowStockThreshold = 5
                    });
                    await db.SaveChangesAsync();
                }

                return harness;
            }
            catch (Exception setupException)
            {
                try
                {
                    await database.DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        $"Sale PostgreSQL harness setup failed and database {database.Name} could not be removed.",
                        setupException,
                        cleanupException);
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync() => await _database.DisposeAsync();

        public sealed class CashSessionOpenPreflightBarrier : DbCommandInterceptor
        {
            private readonly TaskCompletionSource _bothReadsExecuted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _observedReads;

            public int ObservedReads => Volatile.Read(ref _observedReads);

            public override async ValueTask<DbDataReader> ReaderExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                DbDataReader result,
                CancellationToken cancellationToken = default)
            {
                if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    && command.CommandText.Contains("cash_sessions", StringComparison.OrdinalIgnoreCase)
                    && command.CommandText.Contains("status", StringComparison.OrdinalIgnoreCase))
                {
                    if (Interlocked.Increment(ref _observedReads) == 2)
                        _bothReadsExecuted.TrySetResult();

                    await _bothReadsExecuted.Task.WaitAsync(cancellationToken);
                }

                return result;
            }
        }
    }
}
