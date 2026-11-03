using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Application.AccountingCloseRuns.Queries;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Application.AccountingCloseRuns.Commands;
using RetailPOS.Application.AccountingCloseRuns.ReadModels;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Application.VatReports.Queries;
using RetailPOS.Application.VatReports.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingCloseRuns;
using RetailPOS.Infrastructure.Data;
using AccountingPeriodCloseFinancialGateEvaluator =
    RetailPOS.Application.AccountingCloseRuns.Queries.AccountingPeriodCloseFinancialGateEvaluator;

namespace RetailPOS.Tests.Accounting;

public sealed class P307PeriodCloseRunTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<ISender> _sender = new();

    public P307PeriodCloseRunTests()
    {
        _connection.Open(); _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).Options); _db.Database.EnsureCreated();
        _audit.Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetTrialBalanceQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrialBalanceReportReadModel(
                Array.Empty<TrialBalanceRowReadModel>(),
                0m,
                0m));
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetBalanceSheetQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBalanceSheetReadModel());
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetInventoryValuationQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryValuationReadModel(
                Array.Empty<InventoryValuationRowReadModel>(),
                Array.Empty<UnvaluedStockMovementReadModel>(),
                0m,
                0m,
                0m,
                0m,
                1,
                new DateTime(2026, 1, 31)));
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetVatReconciliationQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VatReconciliationReadModel(
                0m, 0m, 0m, "reconciled",
                0m, 0m, 0m, "reconciled",
                0m, 0m, 0m));
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetVatExceptionsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VatExceptionReadModel>());
        _sender.Setup(sender => sender.Send(
                It.IsAny<IRequest<ApReconciliationReadModel>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApReconciliationReadModel(
                new DateTime(2026, 1, 31),
                1,
                "test",
                0m,
                0m,
                0m,
                Array.Empty<string>()));
        _sender.Setup(sender => sender.Send(
                It.IsAny<IRequest<IReadOnlyList<string>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        Seed();
    }

    [Fact]
    public async Task CleanRun_ClosesAtomically_IsIdempotent_AndCanReopenWithReason()
    {
        var preparedId = await PrepareAsync(10, 100);
        Assert.Equal(preparedId, await PrepareAsync(10, 100));
        Assert.Equal(AccountingPeriodStatuses.Locked, (await _db.AccountingPeriods.FindAsync(10L))!.Status);
        var prepared = await GetRunAsync(preparedId);
        await ApproveRunAsync(prepared.Id, 101);
        var approved = await GetRunAsync(prepared.Id);
        Assert.Equal(AccountingPeriodCloseRunStatuses.Approved, approved.Status);
        Assert.Equal(101, approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApproveRunAsync(prepared.Id, 101));
        Assert.True(await ExecuteRunAsync(
            approved.Id,
            "Month close",
            102));
        var executed = await GetRunAsync(approved.Id);
        Assert.Equal(AccountingPeriodCloseRunStatuses.Executed, executed.Status); Assert.Equal(AccountingPeriodStatuses.Closed, (await _db.AccountingPeriods.FindAsync(10L))!.Status);
        Assert.False(await ExecuteRunAsync(executed.Id, null, 102));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReopenRunAsync(executed.Id, null, 103));
        await ReopenRunAsync(executed.Id, "Approved correction", 103);
        var reopened = await GetRunAsync(executed.Id);
        Assert.Equal(AccountingPeriodCloseRunStatuses.Reopened, reopened.Status);
        Assert.Equal(103, reopened.ReopenedBy);
        Assert.Equal(
            AccountingPeriodStatuses.Open,
            (await _db.AccountingPeriods.FindAsync(10L))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReopenRunAsync(executed.Id, "Duplicate reopen", 103));
    }

    [Fact]
    public async Task WarningDoesNotBlock_ButOnlyHistoricalBlockingGateCanReceiveException()
    {
        _db.JournalEntries.Add(new JournalEntry { BusinessId = 1, JournalType = AccountingJournalTypes.ManualJournal, PostingDate = new DateTime(2026, 1, 15), TransactionDate = new DateTime(2026, 1, 15), ReferenceType = "test", ReferenceId = "draft", Status = JournalEntryStatuses.Draft }); await _db.SaveChangesAsync();
        var run = await GetRunAsync(await PrepareAsync(10, 100));
        Assert.Contains(run.Gates, x => x.GateCode == "manual_journal_warning" && x.Status == AccountingCloseGateStatuses.Warning);
        var current = await _db.AccountingPeriodCloseRuns.FindAsync(run.Id); _db.AccountingCloseGateSnapshots.Add(new AccountingCloseGateSnapshot { AccountingPeriodCloseRunId = run.Id, EvaluationSequence = current!.EvaluationSequence, GateCode = "legacy_cutover", Status = AccountingCloseGateStatuses.Blocking, IsHistoricalCutoverLimitation = true, Details = "Legacy cutover exception" }); await _db.SaveChangesAsync();
        var gate = await _db.AccountingCloseGateSnapshots.SingleAsync(x => x.GateCode == "legacy_cutover");
        await ApproveExceptionAsync(
            run.Id,
            gate.Id,
            new ApproveCloseExceptionDto
            {
                Reason = " Finance accepted cutover evidence "
            },
            101);
        var readRun = await new AccountingPeriodCloseRunReadStore(_db).GetAsync(1, run.Id, CancellationToken.None);
        var approvedGate = Assert.Single(readRun.Gates, snapshot => snapshot.Id == gate.Id);
        Assert.True(approvedGate.HasApprovedException);
        Assert.Equal("Finance accepted cutover evidence", approvedGate.ApprovedExceptionReason);
        Assert.Equal(101, approvedGate.ExceptionApprovedBy);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApproveExceptionAsync(
                run.Id,
                gate.Id,
                new ApproveCloseExceptionDto { Reason = "Duplicate decision" },
                102));
        await ApproveRunAsync(run.Id, 101);
        _db.AccountingCloseGateSnapshots.Add(new AccountingCloseGateSnapshot { AccountingPeriodCloseRunId = run.Id, EvaluationSequence = current.EvaluationSequence, GateCode = "integrity", Status = AccountingCloseGateStatuses.Blocking, IsHistoricalCutoverLimitation = false, Details = "Current integrity failure" }); await _db.SaveChangesAsync();
        var integrity = await _db.AccountingCloseGateSnapshots.SingleAsync(x => x.GateCode == "integrity"); await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveExceptionAsync(run.Id, integrity.Id, new ApproveCloseExceptionDto { Reason = "Not eligible" }, 101));
    }

    [Fact]
    public async Task ExactZeroTrialBalanceGate_BlocksApproval_AndTenantCannotReadRun()
    {
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetTrialBalanceQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrialBalanceReportReadModel(
                Array.Empty<TrialBalanceRowReadModel>(),
                1m,
                0m));
        var run = await GetRunAsync(await PrepareAsync(10, 100));
        Assert.Contains(run.Gates, x => x.GateCode == "trial_balance" && x.Status == AccountingCloseGateStatuses.Blocking && x.ActualAmount == 1m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveRunAsync(run.Id, 101));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new AccountingPeriodCloseRunReadStore(_db).GetAsync(
                2,
                run.Id,
                CancellationToken.None));
    }

    [Fact]
    public async Task PeriodCloseRunReadStore_PreservesListOrderFilterAndGateEvidence()
    {
        var createdId = await PrepareAsync(10, 100);
        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 11,
            FinancialYearId = 1,
            PeriodNumber = 2,
            Name = "February",
            StartDate = new DateTime(2026, 2, 1),
            EndDate = new DateTime(2026, 2, 28)
        });
        _db.AccountingPeriodCloseRuns.Add(new AccountingPeriodCloseRun
        {
            BusinessId = 1,
            AccountingPeriodId = 11,
            RunNumber = 1,
            Status = AccountingPeriodCloseRunStatuses.Prepared,
            EvaluationSequence = 1,
            PreparedBy = 100,
            PreparedAt = DateTime.UtcNow,
            CreatedBy = 100,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var store = new AccountingPeriodCloseRunReadStore(_db);

        var listed = await store.ListAsync(1, 10, CancellationToken.None);
        var allRuns = await store.ListAsync(1, null, CancellationToken.None);
        var fetched = await store.GetAsync(1, createdId, CancellationToken.None);
        var filteredOut = await store.ListAsync(1, 999, CancellationToken.None);

        Assert.Equal(2, allRuns.Count);
        Assert.Equal(11, allRuns[0].AccountingPeriodId);
        var listedRun = Assert.Single(listed);
        Assert.Equal(createdId, listedRun.Id);
        Assert.Equal(createdId, fetched.Id);
        Assert.NotEmpty(fetched.Gates);
        Assert.Empty(filteredOut);
    }

    [Fact]
    public async Task PrepareFailure_RollsBackRunAndPeriodLock_ThenRetryAuditsOnce()
    {
        _sender
            .Setup(sender => sender.Send(
                It.IsAny<IRequest<IReadOnlyList<string>>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Gate evaluation failed."));
        var commandStore = new AccountingPeriodCloseRunCommandStore(
            _db,
            CreateFinancialGateEvaluator(),
            _audit.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            commandStore.PrepareAsync(1, 10, 100, CancellationToken.None));
        Assert.Empty(await _db.AccountingPeriodCloseRuns.AsNoTracking().ToListAsync());
        var period = await _db.AccountingPeriods.SingleAsync(value => value.Id == 10);
        await _db.Entry(period).ReloadAsync();
        Assert.Equal(AccountingPeriodStatuses.Open, period.Status);
        _audit.Verify(
            audit => audit.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _sender.Setup(sender => sender.Send(
                It.IsAny<IRequest<IReadOnlyList<string>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        await using var retryDb = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        var createdId = await PrepareAsync(10, 100, retryDb);
        var created = await new AccountingPeriodCloseRunReadStore(retryDb)
            .GetAsync(1, createdId, CancellationToken.None);
        Assert.Equal(1, created.RunNumber);
        Assert.Equal(
            AccountingPeriodStatuses.Locked,
            (await retryDb.AccountingPeriods.SingleAsync(value => value.Id == 10)).Status);
        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close prepared"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReevaluateConflict_RollsBackSequenceAndSnapshots_ThenRetryAuditsOnce()
    {
        var runId = await PrepareAsync(10, 100);
        var initialGateCount = await _db.AccountingCloseGateSnapshots
            .CountAsync(snapshot => snapshot.AccountingPeriodCloseRunId == runId
                && snapshot.EvaluationSequence == 1);

        await using (var conflictingContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            conflictingContext.AccountingCloseGateSnapshots.Add(
                new AccountingCloseGateSnapshot
                {
                    AccountingPeriodCloseRunId = runId,
                    EvaluationSequence = 2,
                    GateCode = "configuration",
                    Status = AccountingCloseGateStatuses.Passed,
                    Details = "Conflict sentinel"
                });
            await conflictingContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                ReevaluateAsync(runId, 101, conflictingContext));
        }

        await using (var verifyContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            var persistedRun = await verifyContext.AccountingPeriodCloseRuns
                .SingleAsync(run => run.Id == runId);
            Assert.Equal(1, persistedRun.EvaluationSequence);
            Assert.Equal(
                1,
                await verifyContext.AccountingCloseGateSnapshots.CountAsync(
                    snapshot => snapshot.AccountingPeriodCloseRunId == runId
                        && snapshot.EvaluationSequence == 2));
            verifyContext.AccountingCloseGateSnapshots.RemoveRange(
                await verifyContext.AccountingCloseGateSnapshots
                    .Where(snapshot => snapshot.AccountingPeriodCloseRunId == runId
                        && snapshot.EvaluationSequence == 2)
                    .ToListAsync());
            await verifyContext.SaveChangesAsync();
        }

        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close re-evaluated"),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await using var retryContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        await ReevaluateAsync(runId, 101, retryContext);
        var reevaluated = await new AccountingPeriodCloseRunReadStore(retryContext)
            .GetAsync(1, runId, CancellationToken.None);

        Assert.Equal(2, reevaluated.EvaluationSequence);
        Assert.Equal(
            initialGateCount,
            reevaluated.Gates.Count(gate => gate.EvaluationSequence == 2));
        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close re-evaluated"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var reevaluationAudit = Assert.Single(
            _audit.Invocations,
            invocation => invocation.Method.Name == nameof(IAuditService.RecordAsync)
                && invocation.Arguments[0] is AuditEventInput input
                && input.Summary == "Accounting period close re-evaluated");
        Assert.Equal(
            2,
            Assert.IsType<AuditEventInput>(reevaluationAudit.Arguments[0])
                .Metadata!["evaluationSequence"]);
    }

    [Fact]
    public async Task ExecutionGateFailure_RollsBackCloseAndEvaluation_ThenRetryCommitsOnce()
    {
        var runId = await PrepareAsync(10, 100);
        await ApproveRunAsync(runId, 101);
        _sender.Setup(sender => sender.Send(
                It.IsAny<GetTrialBalanceQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrialBalanceReportReadModel(
                Array.Empty<TrialBalanceRowReadModel>(),
                1m,
                0m));

        await using (var failedContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ExecuteRunAsync(
                    runId,
                    " Month close ",
                    102,
                    failedContext));
        }

        await using (var verifyContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            var persistedRun = await verifyContext.AccountingPeriodCloseRuns
                .SingleAsync(run => run.Id == runId);
            Assert.Equal(AccountingPeriodCloseRunStatuses.Approved, persistedRun.Status);
            Assert.Equal(1, persistedRun.EvaluationSequence);
            Assert.Equal(
                AccountingPeriodStatuses.Locked,
                (await verifyContext.AccountingPeriods.SingleAsync(
                    period => period.Id == 10)).Status);
            Assert.Empty(await verifyContext.AccountingCloseGateSnapshots
                .Where(snapshot => snapshot.AccountingPeriodCloseRunId == runId
                    && snapshot.EvaluationSequence == 2)
                .ToListAsync());
        }

        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close executed"),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _sender.Setup(sender => sender.Send(
                It.IsAny<GetTrialBalanceQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrialBalanceReportReadModel(
                Array.Empty<TrialBalanceRowReadModel>(),
                0m,
                0m));
        await using var retryContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        Assert.True(await ExecuteRunAsync(
            runId,
            " Month close ",
            102,
            retryContext));
        var executed = await new AccountingPeriodCloseRunReadStore(retryContext)
            .GetAsync(1, runId, CancellationToken.None);
        Assert.Equal(AccountingPeriodCloseRunStatuses.Executed, executed.Status);
        Assert.Equal(2, executed.EvaluationSequence);
        Assert.Equal("Month close", executed.ExecutionReason);
        Assert.Equal(102, executed.ExecutedBy);
        Assert.Equal(
            AccountingPeriodStatuses.Closed,
            (await retryContext.AccountingPeriods.SingleAsync(
                period => period.Id == 10)).Status);
        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close executed"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReopenPersistenceFailure_RollsBackBothStates_ThenRetryAuditsOnce()
    {
        var runId = await PrepareAsync(10, 100);
        await ApproveRunAsync(runId, 101);
        Assert.True(await ExecuteRunAsync(runId, null, 102));
        await _db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER fail_period_close_reopen
            BEFORE UPDATE ON accounting_period_close_runs
            WHEN NEW.status = 'reopened'
            BEGIN
                SELECT RAISE(ABORT, 'reopen failure');
            END;
            """);

        await using (var failedContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                ReopenRunAsync(runId, "Approved correction", 103, failedContext));
        }

        await using (var verifyContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options))
        {
            var run = await verifyContext.AccountingPeriodCloseRuns
                .SingleAsync(value => value.Id == runId);
            var period = await verifyContext.AccountingPeriods
                .SingleAsync(value => value.Id == 10);
            Assert.Equal(AccountingPeriodCloseRunStatuses.Executed, run.Status);
            Assert.Equal(AccountingPeriodStatuses.Closed, period.Status);
            Assert.Null(period.ReopenedAt);
            Assert.Null(run.ReopenReason);
        }
        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close reopened"),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await _db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER fail_period_close_reopen;");
        await using var retryContext = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        await ReopenRunAsync(
            runId,
            " Approved correction ",
            103,
            retryContext);
        var reopened = await GetRunAsync(runId, retryContext);
        Assert.Equal(AccountingPeriodCloseRunStatuses.Reopened, reopened.Status);
        Assert.Equal("Approved correction", reopened.ReopenReason);
        Assert.Equal(103, reopened.ReopenedBy);
        Assert.Equal(
            AccountingPeriodStatuses.Open,
            (await retryContext.AccountingPeriods.SingleAsync(
                value => value.Id == 10)).Status);
        _audit.Verify(
            audit => audit.RecordAsync(
                It.Is<AuditEventInput>(input =>
                    input.Summary == "Accounting period close reopened"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task<long> PrepareAsync(
        long accountingPeriodId,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new PrepareAccountingPeriodCloseRunCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        return await handler.Handle(
            new PrepareAccountingPeriodCloseRunCommand(
                accountingPeriodId,
                actorUserId),
            CancellationToken.None);
    }

    private Task<AccountingPeriodCloseRunReadModel> GetRunAsync(
        long accountingPeriodCloseRunId,
        RetailPOSDbContext? context = null) =>
        new AccountingPeriodCloseRunReadStore(context ?? _db)
            .GetAsync(1, accountingPeriodCloseRunId, CancellationToken.None);

    private async Task ReevaluateAsync(
        long accountingPeriodCloseRunId,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ReevaluateAccountingPeriodCloseRunCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        await handler.Handle(
            new ReevaluateAccountingPeriodCloseRunCommand(
                accountingPeriodCloseRunId,
                actorUserId),
            CancellationToken.None);
    }

    private async Task ApproveExceptionAsync(
        long accountingPeriodCloseRunId,
        long gateSnapshotId,
        ApproveCloseExceptionDto request,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ApproveAccountingPeriodCloseExceptionCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        await handler.Handle(
            new ApproveAccountingPeriodCloseExceptionCommand(
                accountingPeriodCloseRunId,
                gateSnapshotId,
                request.Reason,
                actorUserId),
            CancellationToken.None);
    }

    private async Task ApproveRunAsync(
        long accountingPeriodCloseRunId,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ApproveAccountingPeriodCloseRunCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        await handler.Handle(
            new ApproveAccountingPeriodCloseRunCommand(
                accountingPeriodCloseRunId,
                actorUserId),
            CancellationToken.None);
    }

    private async Task<bool> ExecuteRunAsync(
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ExecuteAccountingPeriodCloseRunCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        return await handler.Handle(
            new ExecuteAccountingPeriodCloseRunCommand(
                accountingPeriodCloseRunId,
                reason,
                actorUserId),
            CancellationToken.None);
    }

    private async Task ReopenRunAsync(
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        RetailPOSDbContext? context = null)
    {
        var dbContext = context ?? _db;
        var scope = new Mock<IAccountingPeriodCloseTenantScope>();
        scope.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ReopenAccountingPeriodCloseRunCommandHandler(
            new AccountingPeriodCloseRunCommandStore(
                dbContext,
                CreateFinancialGateEvaluator(),
                _audit.Object),
            scope.Object);
        await handler.Handle(
            new ReopenAccountingPeriodCloseRunCommand(
                accountingPeriodCloseRunId,
                reason,
                actorUserId),
            CancellationToken.None);
    }

    private AccountingPeriodCloseFinancialGateEvaluator CreateFinancialGateEvaluator() =>
        new(_sender.Object);

    private static BalanceSheetReportReadModel CreateBalanceSheetReadModel() =>
        new(
            new DateTime(2026, 1, 31),
            false,
            null,
            new BalanceSheetSectionReadModel("Assets", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            new BalanceSheetSectionReadModel("Liabilities", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            new BalanceSheetSectionReadModel("Equity", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            0m,
            0m,
            0m,
            0m,
            0m);

    private void Seed()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.ChartAccounts.AddRange(new ChartAccount { Id = 1, BusinessId = 1, AccountCode = "1100", AccountName = "Advance", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit }, new ChartAccount { Id = 2, BusinessId = 1, AccountCode = "2100", AccountName = "Payable", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit }, new ChartAccount { Id = 3, BusinessId = 1, AccountCode = "2200", AccountName = "VAT", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit }, new ChartAccount { Id = 4, BusinessId = 1, AccountCode = "1200", AccountName = "Input VAT", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit });
        _db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, SupplierAdvanceAccountId = 1, SupplierPayableAccountId = 2, VatPayableAccountId = 3, VatReceivableAccountId = 4, InventoryAccountId = 1 });
        _db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        _db.AccountingPeriods.Add(new AccountingPeriod { Id = 10, FinancialYearId = 1, PeriodNumber = 1, Name = "January", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 31) }); _db.SaveChanges();
    }
    public void Dispose() { _db.Dispose(); _connection.Dispose(); }
}