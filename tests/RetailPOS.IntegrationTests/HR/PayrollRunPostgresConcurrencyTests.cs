using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;
using RetailPOS.Infrastructure.HR.Approvals;
using RetailPOS.API.Services;
using RetailPOS.API.Services.HR;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Identity;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollRunPostgresConcurrencyTests
{
    private readonly ITestOutputHelper output;

    public PayrollRunPostgresConcurrencyTests(ITestOutputHelper output) =>
        this.output = output;

    [Theory]
    [InlineData("payment", false)]
    [InlineData("payment", true)]
    [InlineData("reconciliation", false)]
    [InlineData("reconciliation", true)]
    [InlineData("accounting", false)]
    [InlineData("accounting", true)]
    [InlineData("reopen", false)]
    [InlineData("reopen", true)]
    [InlineData("correction", false)]
    [InlineData("correction", true)]
    public async Task PayrollPeriodCloseSerializesWithFinancialMutationsOnPostgres(
        string mutationKind, bool mutationFirst)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString = GetPostgresConnectionString();
        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString, PostgresTestDatabasePrefix.MigrationBaseline);
        long runId;
        long paymentId;
        long allocationId;
        long expensePostingId;
        long originalRunVersion;
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll close races" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.Users.Add(new User
            {
                Id = 101, BusinessId = 1, Name = "Payroll actor",
                Email = $"payroll-close-race-{mutationKind}@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            setup.PayrollMonetaryConfigurationVersions.Add(
                new PayrollMonetaryConfigurationVersion
                {
                    BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                    CurrencyCode = "BDT", DecimalPrecision = 2,
                    RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                    CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
                });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                PayDate = new(2026, 11, 5), Status = PayrollPeriodStatuses.Processing,
                Version = 2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
        }

        await using (var maker = new Session(database.ConnectionString))
        {
            var run = await maker.CreateAsync($"close-race-{mutationKind}-original-run");
            runId = run.Id;
            await maker.CalculateAsync();
        }

        await using (var seed = CreateDb(database.ConnectionString))
        {
            originalRunVersion = await seed.PayrollRuns
                .Where(row => row.Id == runId).Select(row => row.Version).SingleAsync();
            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Approved}, approval_evidence_hash = {new string('A', 64)}, approval_instance_id = {88L}, decided_by_user_id = {101L}, decided_at_utc = {DateTime.UtcNow} WHERE id = {runId}");
            var period = await seed.PayrollPeriods.SingleAsync(row => row.Id == 1);
            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payroll_periods SET status = {PayrollPeriodStatuses.Approved} WHERE id = {period.Id}");
            seed.Entry(period).State = EntityState.Detached;
            seed.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "accounting.enabled", IsEnabled = true
            });
            var result = await seed.PayrollEmployeeResults.SingleAsync(
                row => row.PayrollRunId == runId);
            var payableAmount = result.NetPay
                ?? throw new InvalidOperationException("The close-race Payroll result has no net pay.");
            var payment = new PayrollPayment
            {
                BusinessId = 1, PayrollPeriodId = period.Id, PayrollRunId = runId,
                PayrollEmployeeResultId = result.Id, EmployeeId = result.EmployeeId,
                PayableAmount = payableAmount, PaidAmount = payableAmount,
                OutstandingAmount = 0m, CurrencyCode = "BDT",
                Method = PayrollPaymentMethods.Cash, Status = PayrollPaymentStatuses.Paid,
                Version = 1, IdempotencyKey = $"close-race-{mutationKind}",
                IdempotencyPayloadHash = new string('B', 64), AttemptNo = 1,
                ResolutionStatus = PayrollPaymentResolutionStatuses.None,
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow,
                PaidAtUtc = DateTime.UtcNow
            };
            var allocation = new PayrollPaymentAllocation
            {
                BusinessId = 1, AllocationNo = 1, Method = PayrollPaymentMethods.Cash,
                Amount = payableAmount, SettledAmount = payableAmount,
                State = PayrollPaymentAllocationStates.Settled
            };
            payment.Allocations.Add(allocation);
            var evidence = new PayrollPaymentEvidence
            {
                BusinessId = 1, Type = PayrollPaymentEvidenceTypes.Settlement,
                Amount = payableAmount, EvidenceDate = new(2026, 10, 31),
                EvidenceReference = $"CLOSE-RACE-{mutationKind}",
                AllocationEvidenceJson =
                    $$"""[{"AllocationNo":1,"Method":"Cash","Amount":{{payableAmount}}}]""",
                IdempotencyKey = $"close-race-settlement-{mutationKind}",
                PayloadHash = new string('C', 64), VerifiedByUserId = 101,
                VerifiedAtUtc = DateTime.UtcNow
            };
            payment.Evidence.Add(evidence);
            seed.PayrollPayments.Add(payment);
            await seed.SaveChangesAsync();
            paymentId = payment.Id;
            allocationId = allocation.Id;
            var reconciliation = new PayrollPaymentReconciliation
            {
                BusinessId = 1, PayrollPaymentId = payment.Id,
                PayrollPaymentAllocationId = allocation.Id, Version = 1,
                PaymentVersion = payment.Version, ExpectedAmount = payableAmount,
                SettledAmount = payableAmount, Difference = 0m,
                Status = PayrollPaymentReconciliationStatuses.Reconciled,
                SettlementDate = new(2026, 10, 31),
                EvidenceSource = PayrollPaymentMethods.Cash,
                ExternalReference = $"RECON-{mutationKind}",
                IdempotencyKey = $"close-race-reconciliation-{mutationKind}",
                PayloadHash = new string('D', 64), ReconciledByUserId = 101,
                ReconciledAtUtc = DateTime.UtcNow
            };
            seed.PayrollPaymentReconciliations.Add(reconciliation);
            await seed.SaveChangesAsync();
            var expense = new PayrollAccountingPosting
            {
                BusinessId = 1, PayrollPeriodId = period.Id, PayrollRunId = runId,
                RunRevision = 1, PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                PostingReference = $"CLOSE-RACE-EXPENSE-{mutationKind}",
                IdempotencyKey = $"close-race-expense-{mutationKind}",
                RequestPayloadHash = new string('E', 64), AccountingJournalId = 901,
                Status = PayrollAccountingPostingStatuses.Posted, RequestedByUserId = 101,
                RequestedAtUtc = DateTime.UtcNow, PostedAtUtc = DateTime.UtcNow
            };
            seed.PayrollAccountingPostings.AddRange(expense, new PayrollAccountingPosting
            {
                BusinessId = 1, PayrollPeriodId = period.Id, PayrollRunId = runId,
                RunRevision = 1, PostingType = PayrollAccountingPostingTypes.SalaryPayment,
                PayrollPaymentId = payment.Id, PayrollPaymentAllocationId = allocation.Id,
                PayrollPaymentEvidenceId = evidence.Id,
                PayrollPaymentReconciliationId = reconciliation.Id,
                PostingReference = $"CLOSE-RACE-SETTLEMENT-{mutationKind}",
                IdempotencyKey = $"close-race-settlement-posting-{mutationKind}",
                RequestPayloadHash = new string('F', 64), AccountingJournalId = 902,
                Status = PayrollAccountingPostingStatuses.Posted, RequestedByUserId = 101,
                RequestedAtUtc = DateTime.UtcNow, PostedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            expensePostingId = expense.Id;
        }

        await using var closer = new Session(database.ConnectionString);
        await using var mutator = new Session(database.ConnectionString);
        var previewed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closeRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollPeriodCloseResponse? Response, Exception? Error)> CloseAsync()
        {
            try
            {
                return (await closer.ClosePeriodAsync(
                    $"close-race-close-{mutationKind}-{mutationFirst}", previewed,
                    closeRelease.Task), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        async Task<Exception?> MutateAsync()
        {
            await mutationRelease.Task;
            try
            {
                switch (mutationKind)
                {
                    case "payment":
                    {
                        var payment = await mutator.ReadPaymentAsync(paymentId);
                        await mutator.RecordPaymentEvidenceAsync(paymentId,
                            new PayrollPaymentEvidenceRequest(payment.Version,
                                "close-race-return", new DateOnly(2026, 10, 31),
                                "CLOSE-RACE-RETURN",
                                [new PayrollPaymentAllocationEvidenceInput(
                                    1, payment.PayableAmount)],
                                "Verified return during close race"),
                            PayrollPaymentEvidenceTypes.Return, new string('R', 64));
                        break;
                    }
                    case "reconciliation":
                    {
                        var payment = await mutator.ReadPaymentAsync(paymentId);
                        await mutator.ReconcilePaymentAsync(paymentId, payment.Version, 1,
                            "close-race-reconciliation-mutation", new string('G', 64),
                            "CLOSE-RACE-EXCEPTION", isException: true);
                        break;
                    }
                    case "accounting":
                        await MarkExpensePostingConflictAsync(
                            database.ConnectionString, expensePostingId);
                        break;
                    case "reopen":
                        await mutator.ReopenPeriodAsync(2);
                        break;
                    case "correction":
                        await mutator.CreateCorrectionAsync(
                            runId, "CLOSE-RACE-CORRECTION",
                            "close-race-correction", 2, originalRunVersion);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported close-race mutation: {mutationKind}.");
                }
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var closeTask = CloseAsync();
        await previewed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var mutationTask = MutateAsync();
        (PayrollPeriodCloseResponse? Response, Exception? Error) closeOutcome;
        Exception? mutationError;
        if (mutationFirst)
        {
            mutationRelease.SetResult();
            mutationError = await mutationTask;
            closeRelease.SetResult();
            closeOutcome = await closeTask;
        }
        else
        {
            closeRelease.SetResult();
            mutationRelease.SetResult();
            closeOutcome = await closeTask;
            mutationError = await mutationTask;
        }
        var (closeResponse, closeError) = closeOutcome;

        await using var verify = CreateDb(database.ConnectionString);
        var finalPeriod = await verify.PayrollPeriods.AsNoTracking()
            .SingleAsync(row => row.Id == 1);
        output.WriteLine(
            $"{mutationKind} (mutationFirst={mutationFirst}): " +
            $"winner={(closeResponse is null ? "mutation" : "close")}, " +
            $"loser={(closeResponse is null ? closeError?.GetType().Name : mutationError?.GetType().Name)}, " +
            $"period={finalPeriod.Status}, mutationError={mutationError?.Message}.");
        if (closeResponse is not null)
        {
            Assert.True(closeResponse.Closed);
            Assert.Null(closeError);
            Assert.False(mutationFirst);
            Assert.IsAssignableFrom<Exception>(mutationError);
            Assert.Equal(PayrollPeriodStatuses.Closed, finalPeriod.Status);
            Assert.Single(await verify.PayrollPeriodCloseAssessments.Where(row =>
                row.PayrollPeriodId == 1 && row.ClosedAtUtc != null).ToArrayAsync());
        }
        else
        {
            Assert.IsType<PayrollPeriodConflictException>(closeError);
            Assert.Null(mutationError);
            Assert.NotEqual(PayrollPeriodStatuses.Closed, finalPeriod.Status);
            Assert.Empty(await verify.PayrollPeriodCloseAssessments.Where(row =>
                row.PayrollPeriodId == 1 && row.ClosedAtUtc != null).ToArrayAsync());
        }

        switch (mutationKind)
        {
            case "payment":
                Assert.Equal(closeResponse is null
                        ? PayrollPaymentStatuses.Returned : PayrollPaymentStatuses.Paid,
                    (await verify.PayrollPayments.AsNoTracking()
                        .SingleAsync(row => row.Id == paymentId)).Status);
                break;
            case "reconciliation":
                Assert.Equal(closeResponse is null
                        ? PayrollPaymentReconciliationStatuses.Exception
                        : PayrollPaymentReconciliationStatuses.Reconciled,
                    (await verify.PayrollPaymentReconciliations.AsNoTracking()
                            .Where(row => row.PayrollPaymentId == paymentId)
                            .OrderByDescending(row => row.Version)
                            .FirstAsync()).Status);
                break;
            case "accounting":
                Assert.Equal(closeResponse is null
                        ? PayrollAccountingPostingStatuses.Conflict
                        : PayrollAccountingPostingStatuses.Posted,
                    (await verify.PayrollAccountingPostings.AsNoTracking()
                        .SingleAsync(row => row.Id == expensePostingId)).Status);
                break;
            case "reopen":
                Assert.Equal(closeResponse is null
                        ? PayrollPeriodStatuses.Locked : PayrollPeriodStatuses.Closed,
                    finalPeriod.Status);
                break;
            case "correction":
                Assert.Equal(closeResponse is null ? 2 : 1,
                    await verify.PayrollRuns.CountAsync(row => row.PayrollPeriodId == 1));
                if (closeResponse is null)
                    Assert.Equal(PayrollPeriodStatuses.Processing, finalPeriod.Status);
                break;
        }
    }

    [Fact]
    public async Task ConcurrentPayslipArtifactAndDeliveryWritesAreIdempotent()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
                new Business { Id = 2, Name = "Other payroll" });
            await setup.SaveChangesAsync();
            setup.Users.Add(new User
            {
                Id = 101, BusinessId = 1, Name = "Payroll actor",
                Email = "payslip-postgres@example.invalid", PasswordHash = "test", IsActive = true
            });
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                PayDate = new(2026, 11, 5), Status = PayrollPeriodStatuses.Processing,
                Version = 2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
        }

        await using var maker = new Session(database.ConnectionString);
        await maker.CreateAsync("payslip-concurrency-run");
        await maker.CalculateAsync();
        await using var sourceContext = CreateDb(database.ConnectionString);
        var run = await sourceContext.PayrollRuns.SingleAsync();
        var result = await sourceContext.PayrollEmployeeResults.SingleAsync();
        var employee = await sourceContext.HrEmployees.SingleAsync(row => row.Id == 1);
        employee.WorkEmail = "employee@example.invalid";
        await sourceContext.SaveChangesAsync();
        await sourceContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Approved}, approval_instance_id = {88L}, decided_by_user_id = {202L}, decided_at_utc = {DateTime.UtcNow} WHERE id = {run.Id}");
        var blobStore = new MemoryPayslipBlobStore();

        await using var first = new PayslipSession(database.ConnectionString);
        await using var second = new PayslipSession(database.ConnectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollPayslipArtifactResponse? Result, Exception? Error)> Generate(
            PayslipSession session)
        {
            await start.Task;
            try
            {
                return (await session.CreateGenerationService(blobStore).GenerateAsync(
                    run.Id, 1, CancellationToken.None), null);
            }
            catch (Exception exception) { return (null, exception); }
        }

        var generationOne = Generate(first);
        var generationTwo = Generate(second);
        start.SetResult();
        var generationResults = await Task.WhenAll(generationOne, generationTwo);
        Assert.All(generationResults, item => Assert.Null(item.Error));
        Assert.All(generationResults, item => Assert.NotNull(item.Result));
        Assert.Equal(1, generationResults.Count(item => !item.Result!.IsReplay));
        Assert.Equal(1, generationResults.Count(item => item.Result!.IsReplay));
        Assert.Single(generationResults.Select(item => item.Result!.Id).Distinct());
        Assert.Single(generationResults.Select(item => item.Result!.VersionNo).Distinct());

        await using (var verify = CreateDb(database.ConnectionString))
        {
            var artifact = await verify.PayrollArtifactVersions.SingleAsync();
            Assert.Equal(1, artifact.VersionNo);
            Assert.Equal(1, await verify.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payslip.generated"));
            Assert.True(blobStore.Contains(artifact.StorageKey));
        }
        Assert.Equal(1, blobStore.Count);
        Assert.Equal(1, blobStore.DeletedCount);
        await using var artifactReader = new PayslipSession(database.ConnectionString);
        var firstArtifact = await artifactReader.GetArtifactAsync(
            1, generationResults[0].Result!.Id);
        var candidate = new PayrollPayslipArtifactCandidate(
            firstArtifact.BusinessId, firstArtifact.PayrollRunId, firstArtifact.RunRevision,
            firstArtifact.PayrollEmployeeResultId, firstArtifact.PayrollSnapshotHash,
            firstArtifact.PaymentInfoSnapshotHash, firstArtifact.TemplateVersion,
            "payslip-stage-v2", "test-key", new string('E', 64), 2048, "Clean", 101,
            DateTime.UtcNow, firstArtifact.FileName);

        await using var versionOne = new PayslipSession(database.ConnectionString);
        await using var versionTwo = new PayslipSession(database.ConnectionString);
        var versionStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollPayslipArtifactWriteResult? Result, Exception? Error)> GenerateVersion(
            PayslipSession session, PayrollPayslipArtifactCandidate input)
        {
            await versionStart.Task;
            try { return (await session.CreateArtifactAsync(input), null); }
            catch (Exception exception) { return (null, exception); }
        }

        var versionA = GenerateVersion(versionOne, candidate with
        {
            TemplateVersion = "payslip-v2", StorageKey = "payslip-stage-v2"
        });
        var versionB = GenerateVersion(versionTwo, candidate with
        {
            TemplateVersion = "payslip-v3", StorageKey = "payslip-stage-v3"
        });
        versionStart.SetResult();
        var versionResults = await Task.WhenAll(versionA, versionB);
        Assert.All(versionResults, item => Assert.Null(item.Error));
        Assert.All(versionResults, item => Assert.NotNull(item.Result));
        Assert.All(versionResults, item => Assert.False(item.Result!.IsReplay));
        Assert.Equal(new[] { 2, 3 },
            versionResults.Select(item => item.Result!.Artifact.VersionNo).Order().ToArray());

        await using (var verifyVersions = CreateDb(database.ConnectionString))
        {
            var artifacts = await verifyVersions.PayrollArtifactVersions
                .OrderBy(row => row.VersionNo).ToArrayAsync();
            Assert.Equal(new[] { 1, 2, 3 }, artifacts.Select(row => row.VersionNo));
            Assert.Null(artifacts[0].ReplacesVersionId);
            Assert.Equal(artifacts[0].Id, artifacts[1].ReplacesVersionId);
            Assert.Equal(artifacts[1].Id, artifacts[2].ReplacesVersionId);
            Assert.Equal(candidate.PayrollSnapshotHash, artifacts[0].PayrollSnapshotHash);
            Assert.Equal(candidate.TemplateVersion, artifacts[0].TemplateVersion);
            Assert.Equal(3, await verifyVersions.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payslip.generated"));
        }

        var transport = new GatedPayslipEmailTransport(succeeds: true);
        var emailReplayObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var emailOne = new PayslipSession(database.ConnectionString);
        await using var emailTwo = new PayslipSession(database.ConnectionString);
        var firstEmail = emailOne.CreateEmailService(transport).EmailAsync(
            run.Id, 1, "postgres-email-race", CancellationToken.None);
        await transport.Entered.Task;
        var secondEmail = emailTwo.CreateEmailService(transport, emailReplayObserved).EmailAsync(
            run.Id, 1, "postgres-email-race", CancellationToken.None);
        await emailReplayObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        transport.Release();
        var emailResults = await Task.WhenAll(firstEmail, secondEmail);
        Assert.Equal(1, transport.InvocationCount);
        Assert.Equal(emailResults[0], emailResults[1]);
        Assert.Equal("Sent", emailResults[0].Status);
        Assert.Single(emailResults.Select(result => result.Id).Distinct());

        employee.WorkEmail = "changed-recipient@example.invalid";
        await sourceContext.SaveChangesAsync();
        var conflictTransport = new GatedPayslipEmailTransport(succeeds: true);
        await using var emailConflict = new PayslipSession(database.ConnectionString);
        await Assert.ThrowsAsync<PayrollPayslipIdempotencyConflictException>(() =>
            emailConflict.CreateEmailService(conflictTransport).EmailAsync(
                run.Id, 1, "postgres-email-race", CancellationToken.None));
        Assert.Equal(0, conflictTransport.InvocationCount);

        employee.WorkEmail = "employee@example.invalid";
        await sourceContext.SaveChangesAsync();
        var failingTransport = new GatedPayslipEmailTransport(succeeds: false);
        var failureReplayObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var failedEmailOne = new PayslipSession(database.ConnectionString);
        await using var failedEmailTwo = new PayslipSession(database.ConnectionString);
        var firstFailure = failedEmailOne.CreateEmailService(failingTransport).EmailAsync(
            run.Id, 1, "postgres-email-failure-race", CancellationToken.None);
        await failingTransport.Entered.Task;
        var secondFailure = failedEmailTwo.CreateEmailService(
            failingTransport, failureReplayObserved).EmailAsync(
            run.Id, 1, "postgres-email-failure-race", CancellationToken.None);
        await failureReplayObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        failingTransport.Release();
        var failureResults = await Task.WhenAll(firstFailure, secondFailure);
        Assert.Equal(1, failingTransport.InvocationCount);
        Assert.Equal(failureResults[0], failureResults[1]);
        Assert.Equal("Failed", failureResults[0].Status);
        Assert.Equal("ProviderUnavailable", failureResults[0].FailureCode);

        var winnerId = firstArtifact.Id;
        var delivery = new PayrollPayslipDeliveryWrite(
            1, winnerId, "employee@example.invalid", 101, "same-delivery-key",
            new string('F', 64), DateTime.UtcNow, "Pending", null, null, null);
        await using var deliveryOne = new PayslipSession(database.ConnectionString);
        await using var deliveryTwo = new PayslipSession(database.ConnectionString);
        var deliveryStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollPayslipDeliveryWriteResult? Result, Exception? Error)> AddDelivery(
            PayslipSession session)
        {
            await deliveryStart.Task;
            try { return (await session.AddDeliveryAsync(delivery), null); }
            catch (Exception exception) { return (null, exception); }
        }

        var addOne = AddDelivery(deliveryOne);
        var addTwo = AddDelivery(deliveryTwo);
        deliveryStart.SetResult();
        var deliveryResults = await Task.WhenAll(addOne, addTwo);
        Assert.All(deliveryResults, item => Assert.Null(item.Error));
        Assert.Equal(1, deliveryResults.Count(item => !item.Result!.IsReplay));
        Assert.Equal(1, deliveryResults.Count(item => item.Result!.IsReplay));
        Assert.Single(deliveryResults.Select(item => item.Result!.Delivery.Id).Distinct());
        await using (var verify = CreateDb(database.ConnectionString))
        {
            Assert.Equal(3, await verify.PayrollPayslipDeliveries.CountAsync());
            Assert.Equal(3, await verify.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payslip.email_requested"));
            Assert.Equal(2, await verify.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payslip.email_result"));
        }

        await using var conflictSession = new PayslipSession(database.ConnectionString);
        await Assert.ThrowsAsync<PayrollPayslipIdempotencyConflictException>(() =>
            conflictSession.AddDeliveryAsync(delivery with
            {
                RecipientSnapshot = "other@example.invalid",
                PayloadHash = new string('0', 64)
            }));
        await using var verifyConflict = CreateDb(database.ConnectionString);
        Assert.Equal(3, await verifyConflict.PayrollPayslipDeliveries.CountAsync());
    }

    [Fact]
    public async Task SinglePostgresPayrollFixtureKeepsAllFinancialArtifactsConsistent()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await using var database = await PostgresTestDatabase.CreateAsync(
            GetPostgresConnectionString(), PostgresTestDatabasePrefix.MigrationBaseline);

        long finalSettlementExitCaseId;
        long finalSettlementExitVersion;
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll acceptance" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.Users.AddRange(
                new User
                {
                    Id = 101, BusinessId = 1, Name = "Payroll maker",
                    Email = "payroll-cross-artifact-maker@example.invalid",
                    PasswordHash = "test", IsActive = true
                },
                new User
                {
                    Id = 202, BusinessId = 1, Name = "Payroll approver",
                    Email = "payroll-cross-artifact-approver@example.invalid",
                    PasswordHash = "test", IsActive = true
                });
            setup.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = true
                },
                new BusinessFeatureSetting
                {
                    BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = true
                },
                new BusinessFeatureSetting
                {
                    BusinessId = 1, FeatureKey = "accounting.enabled", IsEnabled = true
                });
            var approvalPolicy = new ApprovalPolicy
            {
                BusinessId = 1, Code = "PAYROLL-CROSS-ARTIFACT",
                ProcessType = ApprovalProcessType.PayrollRun,
                Name = "Cross-artifact Payroll approval", VersionNo = 1,
                ScopeType = ApprovalScopeType.Business, EffectiveFrom = new(2020, 1, 1),
                IsActive = true
            };
            approvalPolicy.Steps.Add(new ApprovalPolicyStep
            {
                Sequence = 1, ApproverType = ApprovalApproverType.User,
                ApproverReference = "202", RequiredApprovals = 1,
                CanReject = true, CanCancel = true
            });
            setup.ApprovalPolicies.Add(approvalPolicy);
            setup.PayrollMonetaryConfigurationVersions.Add(
                new PayrollMonetaryConfigurationVersion
                {
                    BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                    CurrencyCode = "BDT", DecimalPrecision = 2,
                    RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                    CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
                });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            var orgUnit = new HrOrgUnit
            {
                BusinessId = 1, Code = "PAYROLL", Name = "Payroll",
                UnitType = "Department", OperationalLocationProjectionId = 10,
                EffectiveFrom = new(2020, 1, 1), IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = 101
            };
            setup.HrOrgUnits.Add(orgUnit);
            await setup.SaveChangesAsync();
            var position = new HrPosition
            {
                BusinessId = 1, Code = "PAYROLL-ANALYST", Name = "Payroll Analyst",
                OrgUnitId = orgUnit.Id, OperationalLocationProjectionId = 10,
                Status = HrPositionStatus.Filled, EffectiveFrom = new(2020, 1, 1)
            };
            setup.HrPositions.Add(position);
            await setup.SaveChangesAsync();
            setup.HrEmployeePositionAssignments.Add(new HrEmployeePositionAssignment
            {
                BusinessId = 1, EmployeeId = 1, PositionId = position.Id,
                OrgUnitId = orgUnit.Id, AssignmentType = HrEmployeeAssignmentType.Primary,
                EffectiveFrom = new(2020, 1, 1), IsCurrent = true
            });
            var clearanceTemplate = new HrClearanceTemplate
            {
                BusinessId = 1,
                Code = "PAYROLL-CROSS-ARTIFACT-EXIT",
                Name = "Cross-artifact payroll exit",
                ScopeType = HrClearanceTemplateScopeType.Business,
                VersionNo = 1,
                EffectiveFrom = new(2020, 1, 1),
                IsActive = true,
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            };
            setup.HrClearanceTemplates.Add(clearanceTemplate);
            await setup.SaveChangesAsync();
            var exitCase = new HrExitCase
            {
                BusinessId = 1,
                EmployeeId = 1,
                ExitType = "Resignation",
                InitiatedDate = new(2026, 10, 1),
                ProposedLastWorkingDate = new(2026, 10, 31),
                ActualLastWorkingDate = new(2026, 10, 31),
                Reason = "Payroll settlement acceptance",
                Status = "Approved",
                InitiatedByUserId = 101,
                InitiatedAtUtc = DateTime.UtcNow,
                PrimaryLocationProjectionIdSnapshot = 10,
                ClearanceTemplateId = clearanceTemplate.Id,
                ClearanceTemplateVersionNo = 1,
                ClearanceTemplateSnapshotJson = "{}",
                Version = 3
            };
            setup.HrExitCases.Add(exitCase);
            await setup.SaveChangesAsync();
            finalSettlementExitCaseId = exitCase.Id;
            finalSettlementExitVersion = exitCase.Version;
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                PayDate = new(2026, 11, 5), Status = PayrollPeriodStatuses.Processing,
                Version = 2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedCrossArtifactSalaryAsync(setup);
            setup.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
            {
                BusinessId = 1, EmployeeId = 1, VersionNo = 1, Version = 1,
                OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Bank,
                IsPrimary = true, IsActive = true, EffectiveFrom = new(2020, 1, 1),
                BankName = "Acceptance Bank", AccountName = "Payroll Employee",
                AccountNumber = "00987654321", CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        long runId;
        long resultId;
        long employeeId;
        long runVersion;
        await using (var payroll = new Session(database.ConnectionString))
        {
            var run = await payroll.CreateAsync("cross-artifact-payroll-run");
            var calculated = await payroll.CalculateAsync();
            runId = run.Id;
            await using var source = CreateDb(database.ConnectionString);
            var runResponse = await payroll.GetRunAsync(runId);
            var validation = await new PayrollRunValidationStore(source).ValidateAsync(
                1, 1, runResponse, 1, 100, CancellationToken.None);
            Assert.True(validation.CanSubmit,
                string.Join("; ", validation.Findings
                    .Where(finding => finding.Severity == PayrollValidationSeverities.BlockingError)
                    .Select(finding => finding.Message)));
            var approval = await payroll.SubmitAndApproveRunAsync(
                runId, "cross-artifact-run-approved");
            Assert.Equal(ApprovalInstanceStatus.Approved, approval.Status);
            var approvedRun = await source.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == runId);
            Assert.Equal(PayrollRunStatuses.Approved, approvedRun.Status);
            Assert.Equal(202, approvedRun.DecidedByUserId);
            Assert.Equal(approval.InstanceId, approvedRun.ApprovalInstanceId);
            runVersion = approvedRun.Version;
            var result = await source.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.PayrollRunId == runId);
            resultId = result.Id;
            employeeId = result.EmployeeId;
            Assert.Equal(calculated.PayrollRunId, runId);
        }

        await using (var accountingSetup = CreateDb(database.ConnectionString))
        {
            accountingSetup.AccountingConfigurations.Add(new AccountingConfiguration
            {
                BusinessId = 1
            });
            accountingSetup.FinancialYears.Add(new FinancialYear
            {
                Id = 2026, BusinessId = 1, YearCode = "2026", Name = "FY2026",
                StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), IsCurrent = true
            });
            accountingSetup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 1026, FinancialYearId = 2026, PeriodNumber = 10, Name = "October",
                StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31),
                Status = AccountingPeriodStatuses.Open
            });
            var expense = new ChartAccount
            {
                BusinessId = 1, AccountCode = "6100", AccountName = "Payroll expense",
                AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit
            };
            var deductionLiability = new ChartAccount
            {
                BusinessId = 1, AccountCode = "2110", AccountName = "Payroll deductions payable",
                AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit
            };
            var salaryPayable = new ChartAccount
            {
                BusinessId = 1, AccountCode = "2100", AccountName = "Salary payable",
                AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit
            };
            var bankAccount = new ChartAccount
            {
                BusinessId = 1, AccountCode = "1020", AccountName = "Bank",
                AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit
            };
            var cashAccount = new ChartAccount
            {
                BusinessId = 1, AccountCode = "1000", AccountName = "Cash on hand",
                AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit
            };
            accountingSetup.ChartAccounts.AddRange(expense, deductionLiability,
                salaryPayable, bankAccount, cashAccount);
            await accountingSetup.SaveChangesAsync();
            var resultLines = await accountingSetup.PayrollEmployeeResultLines
                .Where(line => line.BusinessId == 1)
                .ToArrayAsync();
            foreach (var line in resultLines)
                accountingSetup.PayrollAccountingComponentMappings.Add(
                    new PayrollAccountingComponentMapping
                    {
                        BusinessId = 1, ComponentType = line.LineType,
                        ComponentCode = line.ComponentCodeSnapshot,
                        AccountId = line.LineType == PayrollAccountingComponentTypes.Earning
                            ? expense.Id : deductionLiability.Id,
                        UpdatedByUserId = 101, UpdatedAtUtc = DateTime.UtcNow, Version = 1
                    });
            accountingSetup.PayrollAccountingComponentMappings.Add(
                new PayrollAccountingComponentMapping
                {
                    BusinessId = 1, ComponentType = PayrollAccountingComponentTypes.NetPayable,
                    ComponentCode = "NET_PAYABLE", AccountId = salaryPayable.Id,
                    UpdatedByUserId = 101, UpdatedAtUtc = DateTime.UtcNow, Version = 1
                });
            accountingSetup.AccountingConfigurations.Single().DefaultBankAccountId = bankAccount.Id;
            accountingSetup.AccountingConfigurations.Single().CashAccountId = cashAccount.Id;
            await accountingSetup.SaveChangesAsync();
        }

        long expensePostingId;
        string expensePayloadHash;
        long expenseJournalId;
        await using (var accounting = new Session(database.ConnectionString))
        {
            var posted = await accounting.CreatePayrollAccountingPostingService()
                .PostAsync(runId, CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, posted.Posting?.Status);
            expensePostingId = posted.Posting!.Id;
            expensePayloadHash = posted.Posting.RequestPayloadHash!;
            expenseJournalId = posted.Posting.AccountingJournalId!.Value;
        }

        PayrollPaymentResponse payment;
        PayrollPaymentResponse firstSettlement;
        PayrollPaymentBatchResponse batch;
        PayrollPaymentBankFileResult bankFile;
        PayrollPaymentResponse settled;
        PayrollPaymentReconciliationResponse[] reconciliations;
        PayrollAccountingPostingResponse settlementPosting;
        PayrollAccountingPostingResponse firstSettlementPosting;
        PayrollAccountingPostingResponse firstBankSettlementPosting;
        await using (var financial = new Session(database.ConnectionString))
        {
            await using var resultDb = CreateDb(database.ConnectionString);
            var persistedResult = await resultDb.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.Id == resultId);
            var payable = persistedResult.NetPay!.Value;
            Assert.Equal(1000m, payable);
            const decimal cashSettled = 400m;
            const decimal bankSettled = 300m;
            var outstandingAfterFirst = payable - cashSettled - bankSettled;
            payment = await financial.CreatePaymentForResultAsync(
                resultId, runVersion,
                [
                    new PayrollPaymentAllocationInput(
                        PayrollPaymentMethods.Cash, cashSettled, "cash-attempt-cross-artifact"),
                    new PayrollPaymentAllocationInput(
                        PayrollPaymentMethods.Bank, bankSettled, "bank-attempt-cross-artifact-1"),
                    new PayrollPaymentAllocationInput(
                        PayrollPaymentMethods.Bank, outstandingAfterFirst, "bank-attempt-cross-artifact-2")
                ]);
            Assert.Equal(payable, payment.PayableAmount);
            Assert.Equal(payable, payment.Allocations.Sum(row => row.Amount));

            batch = await financial.CreateBankBatchAsync(runId, payment);
            bankFile = await financial.GenerateBankFileAsync(batch.Id, batch.Version);
            Assert.Equal(1, bankFile.VersionNo);
            Assert.Equal(payment.Allocations
                .Where(row => row.Method == PayrollPaymentMethods.Bank).Sum(row => row.Amount),
                bankFile.ExpectedAmount);
            var bankFileText = Encoding.UTF8.GetString(bankFile.Content);
            Assert.Contains(payment.Id.ToString(CultureInfo.InvariantCulture), bankFileText);
            batch = await financial.GetBankBatchAsync(batch.Id);
            var submitted = await financial.SubmitBatchAsync(
                batch.Id, batch.Version, "cross-artifact-bank-submit");
            var firstAllocation = payment.Allocations.Single(row =>
                row.Method == PayrollPaymentMethods.Cash);
            var firstBankAllocation = payment.Allocations
                .Single(row => row.ExternalReference == "bank-attempt-cross-artifact-1");
            var lastAllocation = payment.Allocations.OrderBy(row => row.AllocationNo).Last();
            var cashSettlement = await financial.RecordPaymentEvidenceAsync(
                payment.Id,
                new PayrollPaymentEvidenceRequest(
                    submitted.Payments.Single().Version, "cross-artifact-cash-settlement",
                    new DateOnly(2026, 10, 15), "cross-artifact-cash-settlement",
                    [new PayrollPaymentAllocationEvidenceInput(
                        firstAllocation.AllocationNo, firstAllocation.Amount)]),
                PayrollPaymentEvidenceTypes.Settlement,
                new string('D', 64));
            Assert.Equal(PayrollPaymentStatuses.PartiallyPaid, cashSettlement.Status);
            Assert.Equal(cashSettled, cashSettlement.PaidAmount);
            firstSettlement = await financial.RecordPaymentEvidenceAsync(
                payment.Id,
                new PayrollPaymentEvidenceRequest(
                    cashSettlement.Version, "cross-artifact-bank-settlement-first",
                    new DateOnly(2026, 10, 15), "cross-artifact-bank-settlement-first",
                    [new PayrollPaymentAllocationEvidenceInput(
                        firstBankAllocation.AllocationNo, firstBankAllocation.Amount)]),
                PayrollPaymentEvidenceTypes.Settlement,
                new string('F', 64));
            Assert.Equal(PayrollPaymentStatuses.PartiallyPaid, firstSettlement.Status);
            Assert.Equal(cashSettled + bankSettled, firstSettlement.PaidAmount);
            Assert.Equal(outstandingAfterFirst, firstSettlement.OutstandingAmount);
            var firstReconciliationOperation = await financial.ReconcilePaymentAsync(
                payment.Id, firstSettlement.Version, "cross-artifact-reconciliation-first",
                firstAllocation.AllocationNo, firstAllocation.Amount, firstAllocation.Method);
            var firstReconciliation = Assert.Single(firstReconciliationOperation.Items);
            Assert.Equal(firstAllocation.Amount, firstReconciliation.ExpectedAmount);
            Assert.Equal(firstAllocation.Amount, firstReconciliation.SettledAmount);
            Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled, firstReconciliation.Status);
            var firstBankReconciliationOperation = await financial.ReconcilePaymentAsync(
                payment.Id, firstSettlement.Version, "cross-artifact-reconciliation-bank-first",
                firstBankAllocation.AllocationNo, firstBankAllocation.Amount, firstBankAllocation.Method);
            var firstBankReconciliation = Assert.Single(firstBankReconciliationOperation.Items);
            Assert.Equal(firstBankAllocation.Amount, firstBankReconciliation.ExpectedAmount);
            Assert.Equal(firstBankAllocation.Amount, firstBankReconciliation.SettledAmount);
            Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled, firstBankReconciliation.Status);

            var partialFinalSettlement = await financial.CalculateFinalSettlementAsync(
                finalSettlementExitCaseId, finalSettlementExitVersion,
                "cross-artifact-final-settlement-partial");
            Assert.Equal(outstandingAfterFirst, partialFinalSettlement.GrossDue);
            Assert.Equal(0m, partialFinalSettlement.TotalDeductions);
            Assert.Equal(outstandingAfterFirst, partialFinalSettlement.NetSettlement);
            var partialPayrollDue = Assert.Single(partialFinalSettlement.Lines,
                line => line.ComponentCode == "PAYROLL_NET_DUE");
            Assert.Equal(outstandingAfterFirst, partialPayrollDue.Amount);
            Assert.Equal("PayrollEmployeeResult", partialPayrollDue.SourceType);
            Assert.Contains(payment.Id.ToString(CultureInfo.InvariantCulture),
                partialPayrollDue.EvidenceSnapshotJson, StringComparison.Ordinal);
            Assert.Contains(firstSettlement.Evidence.Single(row =>
                    row.EvidenceReference == "cross-artifact-cash-settlement").Id
                    .ToString(CultureInfo.InvariantCulture),
                partialPayrollDue.EvidenceSnapshotJson, StringComparison.Ordinal);
            Assert.Contains(firstSettlement.Evidence.Single(row =>
                    row.EvidenceReference == "cross-artifact-bank-settlement-first").Id
                    .ToString(CultureInfo.InvariantCulture),
                partialPayrollDue.EvidenceSnapshotJson, StringComparison.Ordinal);
            Assert.Contains(PayrollPaymentReconciliationStatuses.Reconciled,
                partialPayrollDue.EvidenceSnapshotJson, StringComparison.Ordinal);

            settled = await financial.RecordPaymentEvidenceAsync(
                payment.Id,
                new PayrollPaymentEvidenceRequest(
                    firstSettlement.Version, "cross-artifact-settlement-remainder",
                    new DateOnly(2026, 10, 16), "cross-artifact-bank-settlement-remainder",
                    [new PayrollPaymentAllocationEvidenceInput(
                        lastAllocation.AllocationNo, lastAllocation.Amount)]),
                PayrollPaymentEvidenceTypes.Settlement,
                new string('E', 64));
            Assert.Equal(PayrollPaymentStatuses.Paid, settled.Status);
            Assert.Equal(payable, settled.PaidAmount);
            Assert.Equal(0m, settled.OutstandingAmount);
            var finalReconciliationOperation = await financial.ReconcilePaymentAsync(
                payment.Id, settled.Version, "cross-artifact-reconciliation-remainder",
                lastAllocation.AllocationNo, lastAllocation.Amount, lastAllocation.Method);
            var finalReconciliation = Assert.Single(finalReconciliationOperation.Items);
            Assert.Equal(lastAllocation.Amount, finalReconciliation.ExpectedAmount);
            Assert.Equal(lastAllocation.Amount, finalReconciliation.SettledAmount);
            Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled, finalReconciliation.Status);
            var settlementEvidence = settled.Evidence.Single(row =>
                row.EvidenceReference == "cross-artifact-bank-settlement-remainder");
            var firstSettlementEvidence = settled.Evidence.Single(row =>
                row.EvidenceReference == "cross-artifact-cash-settlement");
            var firstBankSettlementEvidence = settled.Evidence.Single(row =>
                row.EvidenceReference == "cross-artifact-bank-settlement-first");
            settlementPosting = await financial.CreatePayrollAccountingSettlementPostingService()
                .PostAsync(payment.Id, lastAllocation.Id, settlementEvidence.Id,
                    CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, settlementPosting.Posting?.Status);
            firstSettlementPosting = await financial.CreatePayrollAccountingSettlementPostingService()
                .PostAsync(payment.Id, firstAllocation.Id, firstSettlementEvidence.Id,
                    CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted,
                firstSettlementPosting.Posting?.Status);
            firstBankSettlementPosting = await financial.CreatePayrollAccountingSettlementPostingService()
                .PostAsync(payment.Id, firstBankAllocation.Id, firstBankSettlementEvidence.Id,
                    CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted,
                firstBankSettlementPosting.Posting?.Status);
            reconciliations = [firstReconciliation, firstBankReconciliation, finalReconciliation];

            var fullyPaidFinalSettlement = await financial.CalculateFinalSettlementAsync(
                finalSettlementExitCaseId, finalSettlementExitVersion,
                "cross-artifact-final-settlement-fully-paid");
            Assert.Equal(2, fullyPaidFinalSettlement.RevisionNo);
            Assert.Equal(0m, fullyPaidFinalSettlement.GrossDue);
            Assert.Equal(0m, fullyPaidFinalSettlement.TotalDeductions);
            Assert.Equal(0m, fullyPaidFinalSettlement.NetSettlement);
            Assert.DoesNotContain(fullyPaidFinalSettlement.Lines,
                line => line.ComponentCode == "PAYROLL_NET_DUE");
            Assert.Contains("ALLOWANCE_ACCEPTANCE", fullyPaidFinalSettlement.SnapshotJson,
                StringComparison.Ordinal);
            Assert.Contains("OVERTIME_ACCEPTANCE", fullyPaidFinalSettlement.SnapshotJson,
                StringComparison.Ordinal);

            async Task<PayrollFinalSettlementModel> CalculateConcurrently()
            {
                await using var concurrentSession = new Session(database.ConnectionString);
                return await concurrentSession.CalculateFinalSettlementAsync(
                    finalSettlementExitCaseId, finalSettlementExitVersion,
                    "cross-artifact-final-settlement-concurrent");
            }

            var concurrentFinalSettlements = await Task.WhenAll(
                CalculateConcurrently(), CalculateConcurrently());
            Assert.Single(concurrentFinalSettlements, result => !result.IsReplay);
            Assert.Single(concurrentFinalSettlements, result => result.IsReplay);
            Assert.All(concurrentFinalSettlements, result =>
            {
                Assert.Equal(3, result.RevisionNo);
                Assert.Equal(fullyPaidFinalSettlement.Id, result.Id);
                Assert.Equal(fullyPaidFinalSettlement.NetSettlement, result.NetSettlement);
            });
            Assert.Single(concurrentFinalSettlements
                .Select(result => result.SnapshotHash).Distinct());
        }

        byte[] payslipPdf;
        PayrollPayslipSource payslipSource;
        await using (var payslip = new PayslipSession(database.ConnectionString))
        {
            var memory = new MemoryPayslipBlobStore();
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var payslipService = payslip.CreateGenerationService(
                memory, new PayrollPayslipPdfRenderer(), PayrollRunScopeTypes.Business, null);
            var generatedPayslip = await payslipService.GenerateAsync(
                runId, employeeId, CancellationToken.None);
            await using var payslipReadDb = CreateDb(database.ConnectionString);
            var payslipStore = new PayrollPayslipStore(payslipReadDb);
            payslipSource = await payslipStore.GetSourceAsync(
                1, runId, employeeId, CancellationToken.None);
            var payslipArtifact = await payslipStore.FindLatestAsync(
                1, runId, employeeId, CancellationToken.None);
            Assert.Equal(payslipSource.SnapshotHash, payslipArtifact!.PayrollSnapshotHash);
            var downloadedPayslip = await payslipService.DownloadAsync(
                runId, employeeId, forPrint: false, CancellationToken.None);
            await using var payslipContent = downloadedPayslip.Content;
            await using var payslipBuffer = new MemoryStream();
            await payslipContent.CopyToAsync(payslipBuffer);
            payslipPdf = payslipBuffer.ToArray();
            Assert.Equal(generatedPayslip.ByteLength, payslipPdf.LongLength);
        }
        using var payslipDocument = PdfDocument.Open(payslipPdf);
        var payslipText = string.Join('\n', payslipDocument.GetPages().Select(page => page.Text));

        byte[] csv;
        byte[] xlsx;
        byte[] summaryPdf;
        await using (var exporter = new Session(database.ConnectionString))
        {
            var service = exporter.CreatePayrollExportService();
            csv = await ReadDownloadAsync(await service.CsvAsync(runId, CancellationToken.None));
            xlsx = await ReadDownloadAsync(await service.ExcelAsync(runId, CancellationToken.None));
            summaryPdf = await ReadDownloadAsync(
                await service.PdfSummaryAsync(runId, CancellationToken.None));
        }
        var csvText = Encoding.UTF8.GetString(csv);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(xlsx));
        var worksheet = workbook.Worksheet("Payroll Register");
        using var summaryDocument = PdfDocument.Open(summaryPdf);
        var summaryText = string.Join('\n',
            summaryDocument.GetPages().Select(page => page.Text));

        await using (var verify = CreateDb(database.ConnectionString))
        {
            var run = await verify.PayrollRuns.AsNoTracking().SingleAsync(row => row.Id == runId);
            var result = await verify.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.Id == resultId);
            var lines = await verify.PayrollEmployeeResultLines.AsNoTracking()
                .Where(row => row.PayrollEmployeeResultId == resultId)
                .ToArrayAsync();
            var sourceGross = result.GrossEarnings!.Value;
            var sourceDeductions = result.TotalDeductions!.Value;
            var sourceNet = result.NetPay!.Value;
            var earningLines = lines.Where(line =>
                line.LineType == PayrollAccountingComponentTypes.Earning).ToArray();
            var deductionLines = lines.Where(line =>
                line.LineType == PayrollAccountingComponentTypes.Deduction).ToArray();
            Assert.Contains(earningLines, line => line.ComponentCodeSnapshot == "BASE_ACCEPTANCE");
            Assert.Contains(earningLines, line => line.ComponentCodeSnapshot == "ALLOWANCE_ACCEPTANCE");
            Assert.Contains(earningLines, line => line.ComponentCodeSnapshot == "OVERTIME_ACCEPTANCE");
            Assert.Contains(deductionLines, line => line.ComponentCodeSnapshot == "DEDUCTION_ACCEPTANCE");

            Assert.Equal(sourceGross, payslipSource.GrossEarnings);
            Assert.Equal(sourceDeductions, payslipSource.TotalDeductions);
            Assert.Equal(sourceNet, payslipSource.NetPayable);
            Assert.Contains("Payroll Analyst", payslipText);
            Assert.Contains("Historical location: Outlet", payslipText);
            Assert.Contains(sourceGross.ToString("N2", CultureInfo.InvariantCulture), payslipText);
            Assert.Contains(sourceDeductions.ToString("N2", CultureInfo.InvariantCulture), payslipText);
            Assert.Contains(sourceNet.ToString("N2", CultureInfo.InvariantCulture), payslipText);
            foreach (var line in lines)
                Assert.Contains(line.Amount.ToString("N2", CultureInfo.InvariantCulture), payslipText);

            Assert.Contains(sourceGross.ToString(CultureInfo.InvariantCulture), csvText);
            Assert.Contains(sourceDeductions.ToString(CultureInfo.InvariantCulture), csvText);
            Assert.Contains(sourceNet.ToString(CultureInfo.InvariantCulture), csvText);
            Assert.Contains("Payroll Analyst", csvText);
            Assert.Contains("Outlet", csvText);
            var headers = worksheet.Row(1).CellsUsed()
                .ToDictionary(cell => cell.GetString(), cell => cell.Address.ColumnNumber);
            Assert.Equal(sourceGross,
                worksheet.Cell(2, headers["Gross Earnings"]).GetValue<decimal>());
            Assert.Equal(sourceDeductions,
                worksheet.Cell(2, headers["Total Deductions"]).GetValue<decimal>());
            Assert.Equal(sourceNet,
                worksheet.Cell(2, headers["Net Payable"]).GetValue<decimal>());
            Assert.Contains("Payroll Analyst",
                worksheet.Row(2).CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains("Outlet",
                worksheet.Row(2).CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains(sourceGross.ToString("N2", CultureInfo.InvariantCulture), summaryText);
            Assert.Contains(sourceDeductions.ToString("N2", CultureInfo.InvariantCulture), summaryText);
            Assert.Contains(sourceNet.ToString("N2", CultureInfo.InvariantCulture), summaryText);

            var persistedPayment = await verify.PayrollPayments.AsNoTracking()
                .SingleAsync(row => row.Id == payment.Id);
            var persistedReconciliations = await verify.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.PayrollPaymentId == payment.Id).ToArrayAsync();
            Assert.Equal(sourceNet, persistedPayment.PayableAmount);
            var persistedAllocations = await verify.PayrollPaymentAllocations.AsNoTracking()
                .Where(row => row.PayrollPaymentId == payment.Id).ToArrayAsync();
            Assert.Equal(3, persistedAllocations.Length);
            Assert.Equal(sourceNet, persistedAllocations.Sum(row => row.Amount));
            Assert.All(persistedAllocations, allocation =>
                Assert.Equal(allocation.Amount, allocation.SettledAmount));
            Assert.Equal(sourceNet, persistedPayment.PaidAmount);
            Assert.Equal(0m, persistedPayment.OutstandingAmount);
            Assert.Equal(3, persistedReconciliations.Length);
            Assert.Equal(sourceNet, persistedReconciliations.Sum(row => row.ExpectedAmount));
            Assert.Equal(sourceNet, persistedReconciliations.Sum(row => row.SettledAmount));
            Assert.All(persistedReconciliations, row =>
            {
                Assert.Equal(0m, row.Difference);
                Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled, row.Status);
            });
            var settlementHeader = await verify.PayrollFinalSettlements.AsNoTracking()
                .SingleAsync(row => row.ExitCaseId == finalSettlementExitCaseId);
            Assert.Equal(3, settlementHeader.CurrentRevisionNo);
            var settlementRevisions = await verify.PayrollFinalSettlementRevisions.AsNoTracking()
                .Where(row => row.FinalSettlementId == settlementHeader.Id)
                .OrderBy(row => row.RevisionNo).ToArrayAsync();
            Assert.Equal(3, settlementRevisions.Length);
            var latestSettlementRevisionId = settlementRevisions[settlementRevisions.Length - 1].Id;
            var latestSettlementLines = await verify.PayrollFinalSettlementLines.AsNoTracking()
                .Where(row => row.RevisionId == latestSettlementRevisionId)
                .ToArrayAsync();
            Assert.Equal(latestSettlementLines.Length,
                latestSettlementLines.Select(row => row.LineNo).Distinct().Count());

            var expensePosting = await verify.PayrollAccountingPostings.AsNoTracking()
                .SingleAsync(row => row.Id == expensePostingId);
            Assert.Equal(expensePayloadHash, expensePosting.RequestPayloadHash);
            var expenseInstruction = await verify.Set<FrozenSystemJournalOperation>()
                .AsNoTracking().SingleAsync(row => row.JournalId == expenseJournalId);
            Assert.False(string.IsNullOrWhiteSpace(expenseInstruction.PayloadHash));
            foreach (var line in lines)
                Assert.Contains(line.Amount.ToString(CultureInfo.InvariantCulture),
                    expenseInstruction.CanonicalPayloadJson);
            var expenseJournalLines = await verify.JournalEntryLines.AsNoTracking()
                .Where(row => row.JournalEntryId == expenseJournalId).ToArrayAsync();
            Assert.Equal(expenseJournalLines.Sum(line => line.DebitAmount),
                expenseJournalLines.Sum(line => line.CreditAmount));
            Assert.Equal(sourceGross, expenseJournalLines.Sum(line => line.DebitAmount));

            var settlementJournalIds = new List<long>
            {
                settlementPosting.Posting!.AccountingJournalId!.Value,
                firstSettlementPosting.Posting!.AccountingJournalId!.Value,
                firstBankSettlementPosting.Posting!.AccountingJournalId!.Value
            };
            var settlementJournalLines = await verify.JournalEntryLines.AsNoTracking()
                .Where(row => settlementJournalIds.Contains(row.JournalEntryId)).ToArrayAsync();
            Assert.Equal(sourceNet, settlementJournalLines.Sum(line => line.DebitAmount));
            Assert.Equal(sourceNet, settlementJournalLines.Sum(line => line.CreditAmount));
            var settlementInstruction = await verify.Set<FrozenSystemJournalOperation>()
                .AsNoTracking().Where(row => settlementJournalIds.Contains(row.JournalId))
                .ToArrayAsync();
            Assert.Equal(3, settlementInstruction.Length);
            Assert.All(settlementInstruction, instruction =>
                Assert.False(string.IsNullOrWhiteSpace(instruction.PayloadHash)));

            var bankVersion = await verify.PayrollPaymentBankFileVersions.AsNoTracking()
                .SingleAsync(row => row.PaymentBatchId == batch.Id);
            Assert.Equal(bankFile.ContentHash, bankVersion.ContentHash);
            Assert.Equal(bankFile.VersionNo, bankVersion.VersionNo);
            var persistedBatch = await verify.PayrollPaymentBatches.AsNoTracking()
                .SingleAsync(row => row.Id == batch.Id);
            Assert.Equal(runId, persistedBatch.PayrollRunId);
            Assert.Equal(payment.Id, Assert.Single(batch.Payments).PaymentId);

            var snapshotJson = result.CalculationSnapshotJson!;
            var snapshotHash = result.CalculationSnapshotHash!;
            var expenseHash = expenseInstruction.PayloadHash;
            var settlementHash = settlementInstruction[0].PayloadHash;
            var journalAndPostingReferences = new
            {
                ExpensePostingReference = expenseInstruction.PostingReference,
                SettlementPostingReferences = settlementInstruction
                    .Select(instruction => instruction.PostingReference).ToArray(),
                ExpenseJournalId = expenseJournalId,
                SettlementJournalIds = settlementJournalIds,
                PaymentId = payment.Id,
                BatchId = batch.Id,
                BankFileVersion = bankVersion.VersionNo
            };
            Assert.NotNull(journalAndPostingReferences.ExpensePostingReference);
            Assert.All(journalAndPostingReferences.SettlementPostingReferences,
                reference => Assert.NotNull(reference));

            var employee = await verify.HrEmployees.SingleAsync(row => row.Id == employeeId);
            employee.DisplayName = "Changed employee after approval";
            var location = await verify.OperationalLocationProjections
                .SingleAsync(row => row.Id == 10 && row.BusinessId == 1);
            location.Name = "Changed outlet after approval";
            var position = await verify.HrPositions.SingleAsync(row => row.BusinessId == 1);
            position.Name = "Changed Position";
            var salaryVersion = await verify.PayrollEmployeeSalaryVersions
                .SingleAsync(row => row.BusinessId == 1 && row.EmployeeId == employeeId);
            var salarySnapshotJson = salaryVersion.ComponentSnapshotJson;
            var destination = await verify.PayrollEmployeePaymentDestinations
                .SingleAsync(row => row.BusinessId == 1 && row.EmployeeId == employeeId);
            var destinationAccountNumber = destination.AccountNumber;
            var mapping = await verify.PayrollAccountingComponentMappings
                .FirstAsync(row => row.BusinessId == 1);
            mapping.Version++;
            await verify.SaveChangesAsync();
            destination.AccountNumber = "00111111111";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => verify.SaveChangesAsync());
            await verify.Entry(destination).ReloadAsync();
            salaryVersion.ComponentSnapshotJson = "{}";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => verify.SaveChangesAsync());
            await verify.Entry(salaryVersion).ReloadAsync();

            var historicalResult = await verify.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.Id == resultId);
            Assert.Equal(snapshotHash, historicalResult.CalculationSnapshotHash);
            Assert.Equal(snapshotJson, historicalResult.CalculationSnapshotJson);
            Assert.Equal(destinationAccountNumber, await verify.PayrollEmployeePaymentDestinations
                .AsNoTracking().Where(row => row.Id == destination.Id)
                .Select(row => row.AccountNumber).SingleAsync());
            Assert.Equal(salarySnapshotJson, await verify.PayrollEmployeeSalaryVersions
                .AsNoTracking().Where(row => row.Id == salaryVersion.Id)
                .Select(row => row.ComponentSnapshotJson).SingleAsync());
            var historicalPayslip = await new PayrollPayslipStore(verify)
                .GetSourceAsync(1, runId, employeeId, CancellationToken.None);
            Assert.Equal(payslipSource.PositionSnapshot, historicalPayslip.PositionSnapshot);
            Assert.Equal(payslipSource.HistoricalLocationName, historicalPayslip.HistoricalLocationName);
            Assert.Equal(sourceNet, historicalPayslip.NetPayable);
            Assert.Equal(bankFile.ContentHash, (await verify.PayrollPaymentBankFileVersions
                .AsNoTracking().SingleAsync(row => row.PaymentBatchId == batch.Id)).ContentHash);
            Assert.Equal(expenseHash, (await verify.Set<FrozenSystemJournalOperation>()
                .AsNoTracking().SingleAsync(row => row.JournalId == expenseJournalId)).PayloadHash);
            Assert.Contains(settlementHash,
                settlementInstruction.Select(instruction => instruction.PayloadHash));

            var auditRows = await verify.AuditEvents.AsNoTracking()
                .Where(row => row.BusinessId == 1 && row.ActionType.StartsWith("payroll."))
                .Select(row => new { row.ActionType, row.ActionSummary, row.Metadata })
                .ToArrayAsync();
            Assert.Contains(auditRows, row => row.ActionType == "payroll.payslip.generated");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.export.csv");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.export.excel");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.export.pdfsummary");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.bank_file.generated");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.payment.created");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.payment.reconciled");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.accounting.post_requested");
            Assert.Contains(auditRows, row => row.ActionType == "payroll.accounting.settlement_requested");
            var settlementAuditRows = auditRows.Where(row =>
                row.ActionType == "payroll.final_settlement.calculated").ToArray();
            Assert.Equal(3, settlementAuditRows.Length);
            Assert.All(settlementAuditRows, row =>
            {
                Assert.Null(row.Metadata);
                Assert.DoesNotContain(sourceNet.ToString(CultureInfo.InvariantCulture),
                    row.ActionSummary, StringComparison.Ordinal);
                Assert.DoesNotContain(sourceGross.ToString(CultureInfo.InvariantCulture),
                    row.ActionSummary, StringComparison.Ordinal);
            });
            var auditText = string.Join('\n', auditRows.Select(row =>
                $"{row.ActionType} {row.ActionSummary} {row.Metadata}"));
            Assert.DoesNotContain("00987654321", auditText);
            Assert.DoesNotContain("CanonicalPayloadJson", auditText);
            Assert.DoesNotContain("storageKey", auditText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ConcurrentPayrollAccountingPostingCreatesOneFrozenJournalAndPosting()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.Users.Add(new User
            {
                Id = 101, BusinessId = 1, Name = "Payroll actor",
                Email = "payroll-accounting-postgres@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            setup.Users.Add(new User
            {
                Id = 202, BusinessId = 1, Name = "Payroll approver",
                Email = "payroll-accounting-approver@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            var runApprovalPolicy = new ApprovalPolicy
            {
                BusinessId = 1,
                Code = "PAYROLL-ACCOUNTING-POSTGRES",
                ProcessType = ApprovalProcessType.PayrollRun,
                Name = "Payroll Accounting acceptance approval",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Business,
                EffectiveFrom = new(2020, 1, 1),
                IsActive = true
            };
            runApprovalPolicy.Steps.Add(new ApprovalPolicyStep
            {
                Sequence = 1,
                ApproverType = ApprovalApproverType.User,
                ApproverReference = "202",
                RequiredApprovals = 1,
                CanReject = true,
                CanCancel = true
            });
            setup.ApprovalPolicies.Add(runApprovalPolicy);
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                PayDate = new(2026, 11, 5), Status = PayrollPeriodStatuses.Processing,
                Version = 2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
            setup.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
            {
                BusinessId = 1,
                EmployeeId = 1,
                VersionNo = 1,
                OperationalLocationProjectionId = 10,
                Method = PayrollPaymentDestinationMethods.Cash,
                IsPrimary = true,
                IsActive = true,
                EffectiveFrom = new(2020, 1, 1),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using var creator = new Session(database.ConnectionString);
        await creator.CreateAsync("payroll-accounting-posting");
        await creator.CalculateAsync();
        long runId;
        await using (var source = CreateDb(database.ConnectionString))
        {
            var run = await source.PayrollRuns.AsNoTracking().SingleAsync();
            runId = run.Id;
            source.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "accounting.enabled", IsEnabled = true
            });
            source.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1 });
            source.FinancialYears.Add(new FinancialYear
            {
                Id = 2026, BusinessId = 1, YearCode = "2026", Name = "FY2026",
                StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), IsCurrent = true
            });
            source.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 1026, FinancialYearId = 2026, PeriodNumber = 10, Name = "October",
                StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31),
                Status = AccountingPeriodStatuses.Open
            });
            var expense = new ChartAccount
            {
                BusinessId = 1, AccountCode = "6100", AccountName = "Payroll expense",
                AccountType = ChartAccountTypes.Expense,
                NormalBalanceType = NormalBalanceTypes.Debit
            };
            var liability = new ChartAccount
            {
                BusinessId = 1, AccountCode = "2100", AccountName = "Payroll liability",
                AccountType = ChartAccountTypes.Liability,
                NormalBalanceType = NormalBalanceTypes.Credit
            };
            source.ChartAccounts.AddRange(expense, liability);
            await source.SaveChangesAsync();
            var lines = await source.PayrollEmployeeResultLines
                .Where(line => line.BusinessId == 1)
                .ToArrayAsync();
            foreach (var component in lines.Select(line =>
                         (line.LineType, line.ComponentCodeSnapshot)).Distinct())
                source.PayrollAccountingComponentMappings.Add(new PayrollAccountingComponentMapping
                {
                    BusinessId = 1,
                    ComponentType = component.LineType,
                    ComponentCode = component.ComponentCodeSnapshot,
                    AccountId = component.LineType == PayrollAccountingComponentTypes.Earning
                        ? expense.Id : liability.Id,
                    UpdatedByUserId = 101,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Version = 1
                });
            source.PayrollAccountingComponentMappings.Add(new PayrollAccountingComponentMapping
            {
                BusinessId = 1,
                ComponentType = PayrollAccountingComponentTypes.NetPayable,
                ComponentCode = "NET_PAYABLE",
                AccountId = liability.Id,
                UpdatedByUserId = 101,
                UpdatedAtUtc = DateTime.UtcNow,
                Version = 1
            });
            await source.SaveChangesAsync();
        }
        var originalApproval = await creator.SubmitAndApproveRunAsync(runId, "original-payroll-run");
        Assert.Equal(ApprovalInstanceStatus.Approved, originalApproval.Status);
        await using (var approvedOriginal = CreateDb(database.ConnectionString))
        {
            var approvedRun = await approvedOriginal.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == runId);
            Assert.Equal(PayrollRunStatuses.Approved, approvedRun.Status);
            Assert.Equal(202, approvedRun.DecidedByUserId);
            Assert.Equal(originalApproval.InstanceId, approvedRun.ApprovalInstanceId);
            Assert.NotNull(approvedRun.ApprovalEvidenceHash);
            var approvalInstance = await approvedOriginal.ApprovalInstances.AsNoTracking()
                .SingleAsync(row => row.Id == originalApproval.InstanceId);
            Assert.Equal(approvedRun.ApprovalEvidenceHash, approvalInstance.RequestHash);
            Assert.Single(await approvedOriginal.ApprovalDecisions.AsNoTracking()
                .Where(row => row.ApprovalInstanceId == originalApproval.InstanceId)
                .ToArrayAsync());
        }

        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollAccountingPostingResponse? Response, Exception? Error)> Post(Session session)
        {
            await start.Task;
            try
            {
                return (await session.CreatePayrollAccountingPostingService().PostAsync(
                    runId, CancellationToken.None), null);
            }
            catch (Exception exception) { return (null, exception); }
        }

        var firstPosting = Post(first);
        var secondPosting = Post(second);
        start.SetResult();
        var responses = await Task.WhenAll(firstPosting, secondPosting);
        Assert.All(responses, result => Assert.Null(result.Error));
        Assert.Null(first.PostingException);
        Assert.Null(second.PostingException);
        Assert.All(responses, result =>
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, result.Response?.Posting?.Status));
        Assert.Single(responses.Select(result => result.Response!.Posting!.Id).Distinct());
        Assert.Single(responses.Select(result => result.Response!.Posting!.AccountingJournalId).Distinct());

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Single(await verify.PayrollAccountingPostings.ToArrayAsync());
        Assert.Single(await verify.JournalEntries.Where(entry =>
            entry.ReferenceId == $"payroll-expense:1:{runId}:r1").ToArrayAsync());
        Assert.Single(await verify.Set<FrozenSystemJournalOperation>().ToArrayAsync());
        Assert.Equal(2, await verify.AuditEvents.CountAsync(eventRow =>
            eventRow.ActionType == "payroll.accounting.post_requested"));
        var journal = await verify.JournalEntries.SingleAsync(entry =>
            entry.ReferenceId == $"payroll-expense:1:{runId}:r1");
        var journalLines = await verify.JournalEntryLines
            .Where(line => line.JournalEntryId == journal.Id).ToArrayAsync();
        Assert.Equal(journalLines.Sum(line => line.DebitAmount),
            journalLines.Sum(line => line.CreditAmount));

        var committedExpensePosting = await verify.PayrollAccountingPostings.SingleAsync();
        var expensePostingId = committedExpensePosting.Id;
        var expenseJournalId = committedExpensePosting.AccountingJournalId;
        var expenseReference = committedExpensePosting.AccountingReference;
        var expensePayloadHash = committedExpensePosting.RequestPayloadHash;
        Assert.NotNull(expenseJournalId);
        Assert.False(string.IsNullOrWhiteSpace(expenseReference));
        Assert.False(string.IsNullOrWhiteSpace(expensePayloadHash));

        committedExpensePosting.Status = PayrollAccountingPostingStatuses.Processing;
        committedExpensePosting.AccountingJournalId = null;
        committedExpensePosting.AccountingReference = null;
        committedExpensePosting.ErrorMessage = null;
        committedExpensePosting.PostedAtUtc = null;
        committedExpensePosting.Version++;
        await verify.SaveChangesAsync();

        await using (var recovery = new Session(database.ConnectionString))
        {
            var recovered = await recovery.CreatePayrollAccountingPostingService()
                .PostAsync(runId, CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, recovered.Posting?.Status);
            Assert.Equal(expensePostingId, recovered.Posting?.Id);
            Assert.Equal(expenseJournalId, recovered.Posting?.AccountingJournalId);
            Assert.Equal(expenseReference, recovered.Posting?.AccountingReference);
            Assert.Equal(expensePayloadHash, recovered.Posting?.RequestPayloadHash);
        }

        await using var verifyRecovery = CreateDb(database.ConnectionString);
        var recoveredExpensePosting = await verifyRecovery.PayrollAccountingPostings.SingleAsync();
        Assert.Equal(PayrollAccountingPostingStatuses.Posted, recoveredExpensePosting.Status);
        Assert.Equal(expenseJournalId, recoveredExpensePosting.AccountingJournalId);
        Assert.Equal(expenseReference, recoveredExpensePosting.AccountingReference);
        Assert.Equal(expensePayloadHash, recoveredExpensePosting.RequestPayloadHash);
        Assert.Single(await verifyRecovery.JournalEntries.Where(entry =>
            entry.ReferenceId == $"payroll-expense:1:{runId}:r1").ToArrayAsync());
        Assert.Single(await verifyRecovery.Set<FrozenSystemJournalOperation>().ToArrayAsync());
        Assert.Equal(3, await verifyRecovery.AuditEvents.CountAsync(eventRow =>
            eventRow.ActionType == "payroll.accounting.post_requested"));
    }

    [Fact]
    public async Task ConcurrentPayrollSettlementPostingCreatesOneJournalPerEvidenceAllocationIdentity()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.Users.Add(new User
            {
                Id = 101, BusinessId = 1, Name = "Payroll actor",
                Email = "payroll-settlement-postgres@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            setup.Users.Add(new User
            {
                Id = 202, BusinessId = 1, Name = "Payroll approver",
                Email = "payroll-settlement-approver@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            setup.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = true
                },
                new BusinessFeatureSetting
                {
                    BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = true
                });
            var runApprovalPolicy = new ApprovalPolicy
            {
                BusinessId = 1,
                Code = "PAYROLL-SETTLEMENT-POSTGRES",
                ProcessType = ApprovalProcessType.PayrollRun,
                Name = "Payroll settlement acceptance approval",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Business,
                EffectiveFrom = new(2020, 1, 1),
                IsActive = true
            };
            runApprovalPolicy.Steps.Add(new ApprovalPolicyStep
            {
                Sequence = 1,
                ApproverType = ApprovalApproverType.User,
                ApproverReference = "202",
                RequiredApprovals = 1,
                CanReject = true,
                CanCancel = true
            });
            setup.ApprovalPolicies.Add(runApprovalPolicy);
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                PayDate = new(2026, 11, 5), Status = PayrollPeriodStatuses.Processing,
                Version = 2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
            setup.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
            {
                BusinessId = 1,
                EmployeeId = 1,
                VersionNo = 1,
                OperationalLocationProjectionId = 10,
                Method = PayrollPaymentDestinationMethods.Cash,
                IsPrimary = true,
                IsActive = true,
                EffectiveFrom = new(2020, 1, 1),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using var creator = new Session(database.ConnectionString);
        await creator.CreateAsync("payroll-settlement-posting");
        await creator.CalculateAsync();
        long runId;
        await using (var source = CreateDb(database.ConnectionString))
            runId = await source.PayrollRuns.Select(row => row.Id).SingleAsync();
        var originalApproval = await creator.SubmitAndApproveRunAsync(
            runId, "original-settlement-payroll-run");
        Assert.Equal(ApprovalInstanceStatus.Approved, originalApproval.Status);

        long paymentId;
        long allocationId;
        long secondAllocationId;
        long firstEvidenceId;
        long secondEvidenceId;
        long thirdEvidenceId;
        await using (var source = CreateDb(database.ConnectionString))
        {
            var run = await source.PayrollRuns.SingleAsync();
            runId = run.Id;
            var result = await source.PayrollEmployeeResults.SingleAsync();
            Assert.Equal(1000m, result.NetPay);
            var period = await source.PayrollPeriods.SingleAsync();
            var approvedRun = await source.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == runId);
            Assert.Equal(PayrollRunStatuses.Approved, approvedRun.Status);
            Assert.Equal(originalApproval.InstanceId, approvedRun.ApprovalInstanceId);
            Assert.Equal(202, approvedRun.DecidedByUserId);
            Assert.False(string.IsNullOrWhiteSpace(approvedRun.ApprovalEvidenceHash));
            var approvalInstance = await source.ApprovalInstances.AsNoTracking()
                .SingleAsync(row => row.Id == originalApproval.InstanceId);
            Assert.Equal(approvedRun.ApprovalEvidenceHash, approvalInstance.RequestHash);
            var originalDecision = await source.ApprovalDecisions.AsNoTracking()
                .SingleAsync(row => row.ApprovalInstanceId == originalApproval.InstanceId);
            Assert.Equal(202, originalDecision.ActorRealUserId);
            Assert.Equal(ApprovalDecisionType.Approve, originalDecision.Decision);
            source.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "accounting.enabled", IsEnabled = true
            });
            var cash = new ChartAccount
            {
                BusinessId = 1, AccountCode = "1010", AccountName = "Cash",
                AccountType = ChartAccountTypes.Asset,
                NormalBalanceType = NormalBalanceTypes.Debit
            };
            var bank = new ChartAccount
            {
                BusinessId = 1, AccountCode = "1020", AccountName = "Bank",
                AccountType = ChartAccountTypes.Asset,
                NormalBalanceType = NormalBalanceTypes.Debit
            };
            var liability = new ChartAccount
            {
                BusinessId = 1, AccountCode = "2100", AccountName = "Payroll liability",
                AccountType = ChartAccountTypes.Liability,
                NormalBalanceType = NormalBalanceTypes.Credit
            };
            var expenseAccount = new ChartAccount
            {
                BusinessId = 1, AccountCode = "6100", AccountName = "Payroll expense",
                AccountType = ChartAccountTypes.Expense,
                NormalBalanceType = NormalBalanceTypes.Debit
            };
            source.ChartAccounts.AddRange(cash, bank, liability, expenseAccount);
            await source.SaveChangesAsync();
            source.AccountingConfigurations.Add(new AccountingConfiguration
            {
                BusinessId = 1, CashAccountId = cash.Id, DefaultBankAccountId = bank.Id,
                UpdatedAt = DateTime.UtcNow
            });
            source.FinancialYears.Add(new FinancialYear
            {
                Id = 2026, BusinessId = 1, YearCode = "2026", Name = "FY2026",
                StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), IsCurrent = true
            });
            source.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 1026, FinancialYearId = 2026, PeriodNumber = 10, Name = "October",
                StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31),
                Status = AccountingPeriodStatuses.Open
            });
            var lines = await source.PayrollEmployeeResultLines
                .Where(line => line.BusinessId == 1)
                .ToArrayAsync();
            foreach (var component in lines.Select(line =>
                         (line.LineType, line.ComponentCodeSnapshot)).Distinct())
                source.PayrollAccountingComponentMappings.Add(new PayrollAccountingComponentMapping
                {
                    BusinessId = 1,
                    ComponentType = component.LineType,
                    ComponentCode = component.ComponentCodeSnapshot,
                    AccountId = component.LineType == PayrollAccountingComponentTypes.Earning
                        ? expenseAccount.Id : liability.Id,
                    UpdatedByUserId = 101,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Version = 1
                });
            source.PayrollAccountingComponentMappings.Add(new PayrollAccountingComponentMapping
            {
                BusinessId = 1,
                ComponentType = PayrollAccountingComponentTypes.NetPayable,
                ComponentCode = "NET_PAYABLE",
                AccountId = liability.Id,
                UpdatedByUserId = 101,
                UpdatedAtUtc = DateTime.UtcNow,
                Version = 1
            });
            var payment = new PayrollPayment
            {
                BusinessId = 1,
                PayrollPeriodId = period.Id,
                PayrollRunId = runId,
                PayrollEmployeeResultId = result.Id,
                EmployeeId = result.EmployeeId,
                PayableAmount = 1000m,
                PaidAmount = 1000m,
                OutstandingAmount = 0m,
                CurrencyCode = "BDT",
                Method = PayrollPaymentMethods.Cash,
                Status = PayrollPaymentStatuses.Paid,
                Version = 3,
                IdempotencyKey = "settlement-payment",
                IdempotencyPayloadHash = new string('B', 64),
                ResolutionStatus = PayrollPaymentResolutionStatuses.None,
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            };
            var allocation = new PayrollPaymentAllocation
            {
                BusinessId = 1,
                AllocationNo = 1,
                Method = PayrollPaymentMethods.Cash,
                Amount = 500m,
                SettledAmount = 500m,
                State = PayrollPaymentAllocationStates.Settled
            };
            payment.Allocations.Add(allocation);
            payment.Allocations.Add(new PayrollPaymentAllocation
            {
                BusinessId = 1,
                AllocationNo = 2,
                Method = PayrollPaymentMethods.Cash,
                Amount = 500m,
                SettledAmount = 500m,
                State = PayrollPaymentAllocationStates.Settled
            });
            payment.Evidence.Add(new PayrollPaymentEvidence
            {
                BusinessId = 1,
                Type = PayrollPaymentEvidenceTypes.Settlement,
                Amount = 200m,
                EvidenceDate = new(2026, 10, 11),
                EvidenceReference = "SETTLEMENT-200",
                AllocationEvidenceJson = """[{"AllocationNo":1,"Method":"Cash","Amount":200}]""",
                IdempotencyKey = "settlement-200",
                PayloadHash = new string('C', 64),
                VerifiedByUserId = 101,
                VerifiedAtUtc = DateTime.UtcNow
            });
            payment.Evidence.Add(new PayrollPaymentEvidence
            {
                BusinessId = 1,
                Type = PayrollPaymentEvidenceTypes.Settlement,
                Amount = 500m,
                EvidenceDate = new(2026, 10, 13),
                EvidenceReference = "SETTLEMENT-CASH-500",
                AllocationEvidenceJson = """[{"AllocationNo":2,"Method":"Cash","Amount":500}]""",
                IdempotencyKey = "settlement-cash-500",
                PayloadHash = new string('F', 64),
                VerifiedByUserId = 101,
                VerifiedAtUtc = DateTime.UtcNow
            });
            payment.Evidence.Add(new PayrollPaymentEvidence
            {
                BusinessId = 1,
                Type = PayrollPaymentEvidenceTypes.Settlement,
                Amount = 300m,
                EvidenceDate = new(2026, 10, 12),
                EvidenceReference = "SETTLEMENT-300",
                AllocationEvidenceJson = """[{"AllocationNo":1,"Method":"Cash","Amount":300}]""",
                IdempotencyKey = "settlement-300",
                PayloadHash = new string('D', 64),
                VerifiedByUserId = 101,
                VerifiedAtUtc = DateTime.UtcNow
            });
            source.PayrollPayments.Add(payment);
            await source.SaveChangesAsync();
            paymentId = payment.Id;
            allocationId = allocation.Id;
            secondAllocationId = payment.Allocations.Single(row => row.AllocationNo == 2).Id;
            firstEvidenceId = payment.Evidence.Single(row =>
                row.EvidenceReference == "SETTLEMENT-200").Id;
            secondEvidenceId = payment.Evidence.Single(row =>
                row.EvidenceReference == "SETTLEMENT-300").Id;
            thirdEvidenceId = payment.Evidence.Single(row =>
                row.EvidenceReference == "SETTLEMENT-CASH-500").Id;
            source.PayrollPaymentReconciliations.Add(new PayrollPaymentReconciliation
            {
                BusinessId = 1,
                PayrollPaymentId = paymentId,
                PayrollPaymentAllocationId = allocationId,
                Version = 1,
                PaymentVersion = payment.Version,
                ExpectedAmount = 500m,
                SettledAmount = 500m,
                Difference = 0m,
                Status = PayrollPaymentReconciliationStatuses.Reconciled,
                SettlementDate = new(2026, 10, 12),
                EvidenceSource = PayrollPaymentMethods.Cash,
                ExternalReference = "RECONCILED-500",
                IdempotencyKey = "settlement-reconciliation",
                PayloadHash = new string('E', 64),
                ReconciledByUserId = 101,
                ReconciledAtUtc = DateTime.UtcNow
            });
            source.PayrollPaymentReconciliations.Add(new PayrollPaymentReconciliation
            {
                BusinessId = 1,
                PayrollPaymentId = paymentId,
                PayrollPaymentAllocationId = payment.Allocations.Single(row => row.AllocationNo == 2).Id,
                Version = 1,
                PaymentVersion = payment.Version,
                ExpectedAmount = 500m,
                SettledAmount = 500m,
                Difference = 0m,
                Status = PayrollPaymentReconciliationStatuses.Reconciled,
                SettlementDate = new(2026, 10, 13),
                EvidenceSource = PayrollPaymentMethods.Cash,
                ExternalReference = "RECONCILED-CASH-500",
                IdempotencyKey = "settlement-cash-reconciliation",
                PayloadHash = new string('G', 64),
                ReconciledByUserId = 101,
                ReconciledAtUtc = DateTime.UtcNow
            });
            await source.SaveChangesAsync();
        }

        await using (var expense = new Session(database.ConnectionString))
        {
            var response = await expense.CreatePayrollAccountingPostingService()
                .PostAsync(runId, CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status);
        }

        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollAccountingPostingResponse? Response, Exception? Error)> Post(
            Session session, long evidenceId)
        {
            await start.Task;
            try
            {
                return (await session.CreatePayrollAccountingSettlementPostingService().PostAsync(
                    paymentId, allocationId, evidenceId, CancellationToken.None), null);
            }
            catch (Exception exception) { return (null, exception); }
        }

        var firstPosting = Post(first, firstEvidenceId);
        var secondPosting = Post(second, firstEvidenceId);
        start.SetResult();
        var responses = await Task.WhenAll(firstPosting, secondPosting);
        Assert.All(responses, item => Assert.Null(item.Error));
        Assert.All(responses, item =>
            Assert.True(item.Response?.Posting?.Status == PayrollAccountingPostingStatuses.Posted,
                item.Response?.Posting?.ErrorMessage ?? item.Response?.Message));
        Assert.Single(responses.Select(item => item.Response!.Posting!.Id).Distinct());
        Assert.Single(responses.Select(item => item.Response!.Posting!.AccountingJournalId).Distinct());

        await using (var closeCheck = new Session(database.ConnectionString))
            Assert.False(await closeCheck.TryMarkPeriodPostedAsync(1));

        await using var third = new Session(database.ConnectionString);
        var secondEvidencePosting = await third.CreatePayrollAccountingSettlementPostingService()
            .PostAsync(paymentId, allocationId, secondEvidenceId, CancellationToken.None);
        Assert.True(secondEvidencePosting.Posting?.Status == PayrollAccountingPostingStatuses.Posted,
            secondEvidencePosting.Posting?.ErrorMessage ?? secondEvidencePosting.Message);
        Assert.NotEqual(responses[0].Response!.Posting!.Id, secondEvidencePosting.Posting!.Id);
        var thirdEvidencePosting = await third.CreatePayrollAccountingSettlementPostingService()
            .PostAsync(paymentId, secondAllocationId, thirdEvidenceId, CancellationToken.None);
        Assert.True(thirdEvidencePosting.Posting?.Status == PayrollAccountingPostingStatuses.Posted,
            thirdEvidencePosting.Posting?.ErrorMessage ?? thirdEvidencePosting.Message);
        Assert.NotEqual(secondEvidencePosting.Posting!.Id, thirdEvidencePosting.Posting!.Id);

        await using var verify = CreateDb(database.ConnectionString);
        var postings = await verify.PayrollAccountingPostings
            .Where(row => row.PostingType == PayrollAccountingPostingTypes.SalaryPayment)
            .OrderBy(row => row.PayrollPaymentEvidenceId)
            .ToArrayAsync();
        Assert.Equal(3, postings.Length);
        Assert.Equal(3, postings.Select(row => row.PayrollPaymentEvidenceId).Distinct().Count());
        Assert.Equal(2, postings.Select(row => row.PayrollPaymentAllocationId).Distinct().Count());
        Assert.All(postings, row =>
        {
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, row.Status);
            Assert.NotNull(row.PayrollPaymentReconciliationId);
        });
        var settlementJournals = (await verify.JournalEntries.ToArrayAsync())
            .Where(entry => entry.ReferenceId.StartsWith("payroll-settlement:1:", StringComparison.Ordinal))
            .ToArray();
        Assert.True(settlementJournals.Length == 3,
            string.Join(", ", settlementJournals.Select(entry => entry.ReferenceId)));
        Assert.Equal(PayrollPeriodStatuses.Posted,
            (await verify.PayrollPeriods.SingleAsync()).Status);
        Assert.Equal(4, await verify.Set<FrozenSystemJournalOperation>().CountAsync());
        Assert.Equal(4, await verify.AuditEvents.CountAsync(eventRow =>
            eventRow.ActionType == "payroll.accounting.settlement_requested"));
        Assert.Single(await verify.AuditEvents.Where(eventRow =>
            eventRow.ActionType == "payroll.accounting.post_requested").ToArrayAsync());
        var settlementJournalIds = settlementJournals.Select(entry => entry.Id).ToList();
        var settlementLines = await verify.JournalEntryLines
            .Where(line => settlementJournalIds.Contains(line.JournalEntryId))
            .ToArrayAsync();
        Assert.Equal(1000m, settlementLines.Sum(line => line.DebitAmount));
        Assert.Equal(1000m, settlementLines.Sum(line => line.CreditAmount));

        var committedSettlementPosting = postings[0];
        var settlementPostingId = committedSettlementPosting.Id;
        var settlementJournalId = committedSettlementPosting.AccountingJournalId;
        var settlementReference = committedSettlementPosting.AccountingReference;
        var settlementPayloadHash = committedSettlementPosting.RequestPayloadHash;
        var settlementPaymentId = committedSettlementPosting.PayrollPaymentId!.Value;
        var settlementAllocationId = committedSettlementPosting.PayrollPaymentAllocationId!.Value;
        var settlementEvidenceId = committedSettlementPosting.PayrollPaymentEvidenceId!.Value;
        Assert.NotNull(settlementJournalId);
        Assert.False(string.IsNullOrWhiteSpace(settlementReference));
        Assert.False(string.IsNullOrWhiteSpace(settlementPayloadHash));

        committedSettlementPosting.Status = PayrollAccountingPostingStatuses.Processing;
        committedSettlementPosting.AccountingJournalId = null;
        committedSettlementPosting.AccountingReference = null;
        committedSettlementPosting.ErrorMessage = null;
        committedSettlementPosting.PostedAtUtc = null;
        committedSettlementPosting.Version++;
        await verify.SaveChangesAsync();

        await using (var recovery = new Session(database.ConnectionString))
        {
            var recovered = await recovery.CreatePayrollAccountingSettlementPostingService()
                .PostAsync(settlementPaymentId, settlementAllocationId, settlementEvidenceId,
                    CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, recovered.Posting?.Status);
            Assert.Equal(settlementPostingId, recovered.Posting?.Id);
            Assert.Equal(settlementJournalId, recovered.Posting?.AccountingJournalId);
            Assert.Equal(settlementReference, recovered.Posting?.AccountingReference);
            Assert.Equal(settlementPayloadHash, recovered.Posting?.RequestPayloadHash);
        }

        await using var verifySettlementRecovery = CreateDb(database.ConnectionString);
        var recoveredSettlementPosting = await verifySettlementRecovery.PayrollAccountingPostings
            .SingleAsync(row => row.Id == settlementPostingId);
        Assert.Equal(PayrollAccountingPostingStatuses.Posted, recoveredSettlementPosting.Status);
        Assert.Equal(settlementJournalId, recoveredSettlementPosting.AccountingJournalId);
        Assert.Equal(settlementReference, recoveredSettlementPosting.AccountingReference);
        Assert.Equal(settlementPayloadHash, recoveredSettlementPosting.RequestPayloadHash);
        Assert.Single(await verifySettlementRecovery.JournalEntries.Where(entry =>
            entry.Id == settlementJournalId).ToArrayAsync());
        Assert.Equal(4, await verifySettlementRecovery.Set<FrozenSystemJournalOperation>().CountAsync());
        Assert.Equal(5, await verifySettlementRecovery.AuditEvents.CountAsync(eventRow =>
            eventRow.ActionType == "payroll.accounting.settlement_requested"));

        var runBeforeCorrection = await verifySettlementRecovery.PayrollRuns.AsNoTracking()
            .SingleAsync(row => row.Id == runId);
        var periodBeforeCorrection = await verifySettlementRecovery.PayrollPeriods.AsNoTracking()
            .SingleAsync(row => row.Id == runBeforeCorrection.PayrollPeriodId);
        var resultBeforeCorrection = await verifySettlementRecovery.PayrollEmployeeResults
            .AsNoTracking().SingleAsync(row => row.PayrollRunId == runId);
        var paymentBeforeCorrection = await verifySettlementRecovery.PayrollPayments
            .AsNoTracking().Include(row => row.Allocations).Include(row => row.Evidence)
            .SingleAsync(row => row.Id == paymentId);
        var reconciliationBeforeCorrection =
            await verifySettlementRecovery.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.PayrollPaymentId == paymentId)
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id, row.PayrollPaymentAllocationId, row.Version, row.Status,
                    row.ExpectedAmount, row.SettledAmount, row.ExternalReference,
                    row.PayloadHash
                }).ToArrayAsync();
        var postingBeforeCorrection = await verifySettlementRecovery.PayrollAccountingPostings
            .AsNoTracking().Where(row => row.PayrollRunId == runId)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.PostingType, row.Status, row.PostingReference,
                row.RequestPayloadHash, row.AccountingJournalId, row.AccountingReference
            }).ToArrayAsync();
        var originalExpensePosting = postingBeforeCorrection.Single(row =>
            row.PostingType == PayrollAccountingPostingTypes.PayrollExpense);
        var journalIdsBeforeCorrection = postingBeforeCorrection
            .Select(row => row.AccountingJournalId!.Value).Distinct().ToList();
        var journalsBeforeCorrection = await verifySettlementRecovery.JournalEntries
            .AsNoTracking().Where(row => journalIdsBeforeCorrection.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.ReferenceId, row.ReversalOfJournalEntryId,
                Lines = row.Lines.OrderBy(line => line.Id).Select(line => new
                {
                    line.Id, line.AccountId, line.DebitAmount, line.CreditAmount,
                    line.Description
                }).ToArray()
            }).ToArrayAsync();
        var originalEvidenceBeforeCorrection = System.Text.Json.JsonSerializer.Serialize(new
        {
            Run = new
            {
                runBeforeCorrection.Id, runBeforeCorrection.Status, runBeforeCorrection.Version,
                runBeforeCorrection.CalculationVersion, runBeforeCorrection.CalculationInputHash,
                runBeforeCorrection.ApprovalEvidenceHash
            },
            Result = new
            {
                resultBeforeCorrection.Id, resultBeforeCorrection.CalculationSnapshotHash,
                resultBeforeCorrection.CalculationSnapshotJson, resultBeforeCorrection.GrossEarnings,
                resultBeforeCorrection.TotalDeductions, resultBeforeCorrection.NetPay
            },
            Payment = new
            {
                paymentBeforeCorrection.Id, paymentBeforeCorrection.Status,
                paymentBeforeCorrection.Version, paymentBeforeCorrection.PaidAmount,
                paymentBeforeCorrection.OutstandingAmount, paymentBeforeCorrection.ResolutionStatus,
                Allocations = paymentBeforeCorrection.Allocations.OrderBy(row => row.Id)
                    .Select(row => new
                    {
                        row.Id, row.Amount, row.SettledAmount, row.State, row.ExternalReference
                    }),
                Evidence = paymentBeforeCorrection.Evidence.OrderBy(row => row.Id)
                    .Select(row => new
                    {
                        row.Id, row.Type, row.Amount, row.EvidenceReference,
                        row.AllocationEvidenceJson, row.PayloadHash
                    })
            },
            Reconciliations = reconciliationBeforeCorrection,
            Postings = postingBeforeCorrection,
            Journals = journalsBeforeCorrection
        });

        long correctionRunId;
        await using (var correction = new Session(database.ConnectionString))
        {
            var correctionRun = await correction.CreateCorrectionAsync(
                runId, "CORR-POSTED-IMMUTABILITY", "correction-posted-immutability",
                periodBeforeCorrection.Version, runBeforeCorrection.Version);
            Assert.Equal(PayrollRunTypes.Correction, correctionRun.RunType);
            Assert.Equal(runId, correctionRun.OriginalPayrollRunId);
            Assert.NotEqual(runId, correctionRun.Id);

            var replay = await correction.CreateCorrectionAsync(
                runId, "CORR-POSTED-IMMUTABILITY", "correction-posted-immutability",
                periodBeforeCorrection.Version, runBeforeCorrection.Version);
            Assert.True(replay.IsReplay);
            Assert.Equal(correctionRun.Id, replay.Id);
            correctionRunId = correctionRun.Id;
            await correction.CalculateAsync(
                runBeforeCorrection.PayrollPeriodId, correctionRun.Id);
            var correctionApproval = await correction.SubmitAndApproveRunAsync(
                correctionRun.Id, "corrected-payroll-run");
            Assert.Equal(ApprovalInstanceStatus.Approved, correctionApproval.Status);
        }

        await using (var approvedCorrection = CreateDb(database.ConnectionString))
        {
            var correctedRun = await approvedCorrection.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == correctionRunId);
            Assert.Equal(PayrollRunStatuses.Approved, correctedRun.Status);
            Assert.Equal(202, correctedRun.DecidedByUserId);
            Assert.NotNull(correctedRun.ApprovalInstanceId);
            Assert.NotNull(correctedRun.ApprovalEvidenceHash);
            var correctionApprovalInstance = await approvedCorrection.ApprovalInstances.AsNoTracking()
                .SingleAsync(row => row.Id == correctedRun.ApprovalInstanceId);
            Assert.Equal(correctedRun.ApprovalEvidenceHash, correctionApprovalInstance.RequestHash);
            Assert.Single(await approvedCorrection.ApprovalDecisions.AsNoTracking()
                .Where(row => row.ApprovalInstanceId == correctedRun.ApprovalInstanceId)
                .ToArrayAsync());

            var correctedResult = await approvedCorrection.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.PayrollRunId == correctionRunId);
            var correctedPayslip = await new PayrollPayslipStore(approvedCorrection)
                .GetSourceAsync(1, correctionRunId, correctedResult.EmployeeId,
                    CancellationToken.None);
            var correctedExportStore = new PayrollExportStore(approvedCorrection);
            var correctedExportRun = await correctedExportStore.FindRunAsync(
                1, correctionRunId, CancellationToken.None);
            Assert.NotNull(correctedExportRun);
            var correctedEmployees = await correctedExportStore.GetEmployeesAsync(
                1, correctionRunId, correctedExportRun!.Revision, null, false,
                CancellationToken.None);
            var correctedEmployee = Assert.Single(correctedEmployees);
            Assert.Equal(correctedResult.EmployeeCodeSnapshot, correctedPayslip.EmployeeCode);
            Assert.Equal(correctedResult.EmployeeNameSnapshot, correctedPayslip.EmployeeName);
            Assert.Equal(correctedResult.GrossEarnings, correctedPayslip.GrossEarnings);
            Assert.Equal(correctedResult.TotalDeductions, correctedPayslip.TotalDeductions);
            Assert.Equal(correctedResult.NetPay, correctedPayslip.NetPayable);
            Assert.Equal(correctedPayslip.GrossEarnings, correctedEmployee.GrossEarnings);
            Assert.Equal(correctedPayslip.TotalDeductions, correctedEmployee.TotalDeductions);
            Assert.Equal(correctedPayslip.NetPayable, correctedEmployee.NetPayable);
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var correctedPayslipPdf = new PayrollPayslipPdfRenderer()
                .Render(correctedPayslip, "payslip-v1");
            using var correctedPdf = PdfDocument.Open(correctedPayslipPdf);
            var correctedPdfText = string.Join('\n',
                correctedPdf.GetPages().Select(page => page.Text));
            Assert.Contains(correctedPayslip.NetPayable.ToString(
                $"N{correctedPayslip.DecimalPrecision}",
                System.Globalization.CultureInfo.InvariantCulture), correctedPdfText);
            var correctedExportSource = new PayrollExportSource(
                correctedExportRun,
                correctedEmployees,
                false,
                correctedEmployees.Sum(row => row.GrossEarnings),
                correctedEmployees.Sum(row => row.TotalDeductions),
                correctedEmployees.Sum(row => row.NetPayable),
                null,
                null,
                [],
                []);
            var correctedCsv = System.Text.Encoding.UTF8.GetString(
                new PayrollExportRenderer().RenderCsv(correctedExportSource));
            Assert.Contains(correctedPayslip.NetPayable.ToString(
                System.Globalization.CultureInfo.InvariantCulture), correctedCsv);
        }

        await using (var correctionRecovery = new Session(database.ConnectionString))
        {
            var reversalId = await correctionRecovery.CommitCorrectionReversalAsync(
                correctionRunId, originalExpensePosting.Id,
                originalExpensePosting.AccountingJournalId!.Value,
                "CORR-POSTED-IMMUTABILITY");
            await using var verifyReversal = CreateDb(database.ConnectionString);
            var committedReversal = await verifyReversal.JournalEntries.AsNoTracking()
                .SingleAsync(row => row.Id == reversalId);
            Assert.Equal(JournalEntryStatuses.Posted, committedReversal.Status);
            Assert.Equal(originalExpensePosting.AccountingJournalId,
                committedReversal.ReversalOfJournalEntryId);
            Assert.Single(await verifyReversal.Set<FrozenSystemJournalOperation>()
                .Where(row => row.Kind == "Reverse"
                    && row.PostingReference == $"paycorr:1:{correctionRunId}:{originalExpensePosting.Id}:reversal")
                .ToArrayAsync());
        }

        await using (var beforeAuditFailure = CreateDb(database.ConnectionString))
        {
            var journalIds = await beforeAuditFailure.JournalEntries.AsNoTracking()
                .Select(row => row.Id).OrderBy(id => id).ToArrayAsync();
            var auditFailure = new RoleAuditFailureInterceptor
            {
                FailAuditActionType = "payroll.accounting.correction_requested"
            };
            await using var failedAuditSession = new Session(
                database.ConnectionString, auditFailure);
            var failedAuditResponse = await failedAuditSession
                .CreatePayrollAccountingPostingService(
                    new ResolvedPayrollAccountingCorrectionPolicy())
                .PostAsync(correctionRunId, CancellationToken.None);
            Assert.Null(failedAuditResponse.Posting);

            await using var afterAuditFailure = CreateDb(database.ConnectionString);
            Assert.Equal(journalIds, await afterAuditFailure.JournalEntries.AsNoTracking()
                .Select(row => row.Id).OrderBy(id => id).ToArrayAsync());
            Assert.Empty(await afterAuditFailure.PayrollAccountingPostings.AsNoTracking()
                .Where(row => row.PayrollRunId == correctionRunId
                    && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection)
                .ToArrayAsync());
            Assert.Empty(await afterAuditFailure.AuditEvents.AsNoTracking()
                .Where(row => row.ActionType == "payroll.accounting.correction_requested")
                .ToArrayAsync());
            Assert.Single(await afterAuditFailure.AuditEvents.AsNoTracking()
                .Where(row => row.ActionType == "payroll.accounting.correction_failed")
                .ToArrayAsync());
        }

        using (var apiFactory = new PayrollAccountingPostgresApiFactory(database.ConnectionString))
        using (var apiClient = apiFactory.CreateClient())
        {
            apiClient.DefaultRequestHeaders.Add("X-Test-UserId", "101");
            apiClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
            apiClient.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
            var endpoint = $"/api/payroll/v1/runs/{correctionRunId}/post-accounting";

            await using (var beforeDeniedRequest = CreateDb(database.ConnectionString))
            {
                var auditCount = await beforeDeniedRequest.AuditEvents.CountAsync();
                var journalCount = await beforeDeniedRequest.JournalEntries.CountAsync();
                apiClient.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.salary.view");
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await apiClient.PostAsync(endpoint, null)).StatusCode);
                apiClient.DefaultRequestHeaders.Remove("X-Test-Permissions");
                apiClient.DefaultRequestHeaders.Add("X-Test-Permissions",
                    "payroll.accounting.post,payroll.salary.view");
                apiClient.DefaultRequestHeaders.Remove("X-Test-BusinessId");
                apiClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await apiClient.PostAsync(endpoint, null)).StatusCode);
                apiClient.DefaultRequestHeaders.Remove("X-Test-BusinessId");
                apiClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");

                await using (var seedLocationUser = CreateDb(database.ConnectionString))
                {
                    seedLocationUser.Outlets.Add(new Outlet
                    {
                        Id = 20,
                        BusinessId = 1,
                        Name = "Additional outlet"
                    });
                    seedLocationUser.OperationalLocationProjections.Add(
                        new OperationalLocationProjection
                        {
                            Id = 21,
                            BusinessId = 1,
                            SourceSystem = "RetailPOS",
                            SourceEntityType = "Outlet",
                            SourceEntityId = "20",
                            Name = "Additional outlet",
                            SourceVersion = 1
                        });
                    seedLocationUser.Users.Add(new User
                    {
                        Id = 303,
                        BusinessId = 1,
                        Name = "Additional-location-only user",
                        Email = "payroll-correction-accounting-location@example.invalid",
                        PasswordHash = "test",
                        IsActive = true,
                        OutletId = 20,
                        InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
                    });
                    seedLocationUser.UserOutletAssignments.Add(new UserOutletAssignment
                    {
                        UserId = 303,
                        BusinessId = 1,
                        OutletId = 20,
                        IsPrimary = true
                    });
                    seedLocationUser.HrEmployeeLocationAssignments.Add(
                        new HrEmployeeLocationAssignment
                        {
                            BusinessId = 1,
                            EmployeeId = 1,
                            OperationalLocationProjectionId = 21,
                            AssignmentType = "Additional",
                            EffectiveFrom = new(2026, 1, 1),
                            LocationNameSnapshot = "Additional outlet",
                            SourceSystemSnapshot = "RetailPOS",
                            SourceEntityTypeSnapshot = "Outlet",
                            SourceEntityIdSnapshot = "20",
                            LocationSourceVersionSnapshot = 1,
                            RequestedByUserId = 101,
                            RequestedAtUtc = DateTime.UtcNow
                        });
                    await seedLocationUser.SaveChangesAsync();
                }

                apiClient.DefaultRequestHeaders.Remove("X-Test-UserId");
                apiClient.DefaultRequestHeaders.Add("X-Test-UserId", "303");
                apiClient.DefaultRequestHeaders.Remove("X-Test-Role");
                apiClient.DefaultRequestHeaders.Add("X-Test-Role", "HR");
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await apiClient.PostAsync(endpoint, null)).StatusCode);

                await using var afterDeniedRequest = CreateDb(database.ConnectionString);
                Assert.Empty(await afterDeniedRequest.PayrollAccountingPostings.AsNoTracking()
                    .Where(row => row.PayrollRunId == correctionRunId
                        && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection)
                    .ToArrayAsync());
                Assert.Equal(journalCount,
                    await afterDeniedRequest.JournalEntries.CountAsync());
                Assert.Equal(auditCount, await afterDeniedRequest.AuditEvents.CountAsync());
            }

            using var authorizedClient = apiFactory.CreateClient();
            authorizedClient.DefaultRequestHeaders.Add("X-Test-UserId", "101");
            authorizedClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
            authorizedClient.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
            authorizedClient.DefaultRequestHeaders.Add("X-Test-Permissions",
                "payroll.accounting.post,payroll.salary.view");
            var apiResponse = await authorizedClient.PostAsync(endpoint, null);
            Assert.True(apiResponse.StatusCode == HttpStatusCode.OK,
                await apiResponse.Content.ReadAsStringAsync());
            var apiPosting = (await apiResponse.Content
                .ReadFromJsonAsync<PayrollAccountingPostingResponse>())!;
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, apiPosting.Posting?.Status);
            Assert.Equal(PayrollAccountingPostingTypes.PayrollCorrection,
                apiPosting.Posting?.PostingType);
        }

        await using var correctionPostingOne = new Session(database.ConnectionString);
        await using var correctionPostingTwo = new Session(database.ConnectionString);
        var correctionStart = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayrollAccountingPostingResponse> PostCorrection(Session session)
        {
            await correctionStart.Task;
            return await session.CreatePayrollAccountingPostingService(
                    new ResolvedPayrollAccountingCorrectionPolicy())
                .PostAsync(correctionRunId, CancellationToken.None);
        }
        var correctionPostOne = PostCorrection(correctionPostingOne);
        var correctionPostTwo = PostCorrection(correctionPostingTwo);
        correctionStart.SetResult();
        var correctionResponses = await Task.WhenAll(correctionPostOne, correctionPostTwo);
        Assert.All(correctionResponses, response =>
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, response.Posting?.Status));
        Assert.Single(correctionResponses.Select(response => response.Posting!.Id).Distinct());

        await using (var correctionRetry = new Session(database.ConnectionString))
        {
            var replayed = await correctionRetry
                .CreatePayrollAccountingPostingService(
                    new ResolvedPayrollAccountingCorrectionPolicy())
                .PostAsync(correctionRunId, CancellationToken.None);
            Assert.Equal(PayrollAccountingPostingStatuses.Posted, replayed.Posting?.Status);
            Assert.Equal(PayrollAccountingPostingTypes.PayrollCorrection,
                replayed.Posting?.PostingType);
        }

        await using var verifyAfterCorrection = CreateDb(database.ConnectionString);
        var runAfterCorrection = await verifyAfterCorrection.PayrollRuns.AsNoTracking()
            .SingleAsync(row => row.Id == runId);
        var resultAfterCorrection = await verifyAfterCorrection.PayrollEmployeeResults
            .AsNoTracking().SingleAsync(row => row.PayrollRunId == runId);
        var paymentAfterCorrection = await verifyAfterCorrection.PayrollPayments
            .AsNoTracking().Include(row => row.Allocations).Include(row => row.Evidence)
            .SingleAsync(row => row.Id == paymentId);
        var reconciliationAfterCorrection =
            await verifyAfterCorrection.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.PayrollPaymentId == paymentId)
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id, row.PayrollPaymentAllocationId, row.Version, row.Status,
                    row.ExpectedAmount, row.SettledAmount, row.ExternalReference,
                    row.PayloadHash
                }).ToArrayAsync();
        var postingAfterCorrection = await verifyAfterCorrection.PayrollAccountingPostings
            .AsNoTracking().Where(row => row.PayrollRunId == runId)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.PostingType, row.Status, row.PostingReference,
                row.RequestPayloadHash, row.AccountingJournalId, row.AccountingReference
            }).ToArrayAsync();
        var journalIdsAfterCorrection = postingAfterCorrection
            .Select(row => row.AccountingJournalId!.Value).Distinct().ToList();
        var journalsAfterCorrection = await verifyAfterCorrection.JournalEntries
            .AsNoTracking().Where(row => journalIdsAfterCorrection.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.ReferenceId, row.ReversalOfJournalEntryId,
                Lines = row.Lines.OrderBy(line => line.Id).Select(line => new
                {
                    line.Id, line.AccountId, line.DebitAmount, line.CreditAmount,
                    line.Description
                }).ToArray()
            }).ToArrayAsync();
        var originalEvidenceAfterCorrection = System.Text.Json.JsonSerializer.Serialize(new
        {
            Run = new
            {
                runAfterCorrection.Id, runAfterCorrection.Status, runAfterCorrection.Version,
                runAfterCorrection.CalculationVersion, runAfterCorrection.CalculationInputHash,
                runAfterCorrection.ApprovalEvidenceHash
            },
            Result = new
            {
                resultAfterCorrection.Id, resultAfterCorrection.CalculationSnapshotHash,
                resultAfterCorrection.CalculationSnapshotJson, resultAfterCorrection.GrossEarnings,
                resultAfterCorrection.TotalDeductions, resultAfterCorrection.NetPay
            },
            Payment = new
            {
                paymentAfterCorrection.Id, paymentAfterCorrection.Status,
                paymentAfterCorrection.Version, paymentAfterCorrection.PaidAmount,
                paymentAfterCorrection.OutstandingAmount, paymentAfterCorrection.ResolutionStatus,
                Allocations = paymentAfterCorrection.Allocations.OrderBy(row => row.Id)
                    .Select(row => new
                    {
                        row.Id, row.Amount, row.SettledAmount, row.State, row.ExternalReference
                    }),
                Evidence = paymentAfterCorrection.Evidence.OrderBy(row => row.Id)
                    .Select(row => new
                    {
                        row.Id, row.Type, row.Amount, row.EvidenceReference,
                        row.AllocationEvidenceJson, row.PayloadHash
                    })
            },
            Reconciliations = reconciliationAfterCorrection,
            Postings = postingAfterCorrection,
            Journals = journalsAfterCorrection
        });
        Assert.Equal(originalEvidenceBeforeCorrection, originalEvidenceAfterCorrection);
        Assert.Equal(PayrollPaymentStatuses.Paid, paymentAfterCorrection.Status);
        Assert.Equal(1000m, paymentAfterCorrection.PaidAmount);
        Assert.Equal(0m, paymentAfterCorrection.OutstandingAmount);
        var correctionAccountingPosting = await verifyAfterCorrection.PayrollAccountingPostings
            .AsNoTracking().SingleAsync(row => row.PayrollRunId == correctionRunId
                && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection);
        Assert.Equal(PayrollAccountingPostingStatuses.Posted, correctionAccountingPosting.Status);
        Assert.Equal("CORR-POSTED-IMMUTABILITY",
            correctionAccountingPosting.CorrectionReference);
        Assert.Equal(originalExpensePosting.Id, correctionAccountingPosting.OriginalPostingId);
        Assert.NotNull(correctionAccountingPosting.ReversalJournalId);
        Assert.NotNull(correctionAccountingPosting.AccountingJournalId);
        Assert.NotEqual(correctionAccountingPosting.ReversalJournalId,
            correctionAccountingPosting.AccountingJournalId);
        var reversedOriginalJournal = await verifyAfterCorrection.JournalEntries.AsNoTracking()
            .SingleAsync(row => row.Id == originalExpensePosting.AccountingJournalId);
        Assert.Equal(JournalEntryStatuses.Reversed, reversedOriginalJournal.Status);
        Assert.Equal(correctionAccountingPosting.ReversalJournalId,
            reversedOriginalJournal.ReversedByJournalEntryId);
        var correctionReversal = await verifyAfterCorrection.JournalEntries.AsNoTracking()
            .SingleAsync(row => row.Id == correctionAccountingPosting.ReversalJournalId);
        Assert.Equal(originalExpensePosting.AccountingJournalId,
            correctionReversal.ReversalOfJournalEntryId);
        var correctionReplacement = await verifyAfterCorrection.JournalEntries.AsNoTracking()
            .Include(row => row.Lines)
            .SingleAsync(row => row.Id == correctionAccountingPosting.AccountingJournalId);
        Assert.Equal(correctionAccountingPosting.PostingReference, correctionReplacement.ReferenceId);
        Assert.Equal(correctionReplacement.Lines.Sum(line => line.DebitAmount),
            correctionReplacement.Lines.Sum(line => line.CreditAmount));
        var approvedCorrectionResult = await verifyAfterCorrection.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.PayrollRunId == correctionRunId);
        Assert.Equal(approvedCorrectionResult.GrossEarnings,
            correctionReplacement.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(approvedCorrectionResult.NetPay,
            correctionReplacement.Lines.Sum(line => line.CreditAmount));
        var correctionOperations = await verifyAfterCorrection.Set<FrozenSystemJournalOperation>()
            .AsNoTracking().Where(row => row.PostingReference.StartsWith("paycorr:1:"))
            .OrderBy(row => row.Kind).ToArrayAsync();
        Assert.Equal(2, correctionOperations.Length);
        var reversalOperation = Assert.Single(correctionOperations,
            row => row.Kind == "Reverse");
        Assert.Equal(correctionReversal.Id, reversalOperation.JournalId);
        Assert.Equal(originalExpensePosting.AccountingJournalId,
            reversalOperation.OriginalJournalId);
        var replacementOperation = Assert.Single(correctionOperations,
            row => row.Kind == "Post");
        Assert.Equal(correctionReplacement.Id, replacementOperation.JournalId);
        Assert.Equal(correctionAccountingPosting.RequestPayloadHash,
            replacementOperation.PayloadHash);
        Assert.Equal(2, await verifyAfterCorrection.PayrollRuns.CountAsync(row =>
            row.PayrollPeriodId == runBeforeCorrection.PayrollPeriodId));
        Assert.Equal(1, await verifyAfterCorrection.PayrollPeriodReopenEvents
            .CountAsync(row => row.OriginalPayrollRunId == runId));
        Assert.Equal(PayrollAccountingPostingStatuses.Posted,
            (await verifyAfterCorrection.PayrollAccountingPostings.AsNoTracking()
                .SingleAsync(row => row.PayrollRunId == correctionRunId
                    && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection))
            .Status);
        var persistedCorrectionPosting = await verifyAfterCorrection.PayrollAccountingPostings
            .AsNoTracking().SingleAsync(row => row.PayrollRunId == correctionRunId
                && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection);
        Assert.Equal("TEST-ADR-06-ReversalAndReplacement-v1",
            persistedCorrectionPosting.CorrectionPolicyVersion);
        Assert.Equal(4, await verifyAfterCorrection.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.accounting.correction_requested"));
        Assert.Empty(await verifyAfterCorrection.AuditEvents.Where(row =>
            row.ActionType == "payroll.accounting.correction_conflict").ToArrayAsync());
        Assert.Single(await verifyAfterCorrection.AuditEvents.Where(row =>
            row.ActionType == "payroll.accounting.correction_reverse").ToArrayAsync());
        var correctionAudit = await verifyAfterCorrection.AuditEvents.AsNoTracking()
            .Where(row => row.ActionType.StartsWith("payroll.accounting.correction_"))
            .ToArrayAsync();
        Assert.NotEmpty(correctionAudit);
        Assert.All(correctionAudit, row =>
        {
            Assert.Null(row.Metadata);
            Assert.DoesNotContain("1000", row.ActionSummary, StringComparison.Ordinal);
        });
        var accountingLineageBeforeReturn = System.Text.Json.JsonSerializer.Serialize(new
        {
            Postings = postingAfterCorrection,
            Journals = journalsAfterCorrection
        });
        var partialReturn = await creator.RecordPaymentEvidenceAsync(
            paymentId,
            new PayrollPaymentEvidenceRequest(
                paymentBeforeCorrection.Version, "return-posted-lineage",
                new DateOnly(2026, 10, 20), "RETURN-PARTIAL-200",
                [new PayrollPaymentAllocationEvidenceInput(1, 200m)], "Partial verified return"),
            PayrollPaymentEvidenceTypes.Return,
            new string('R', 64));
        Assert.Equal(PayrollPaymentStatuses.Returned, partialReturn.Status);
        Assert.Equal(800m, partialReturn.PaidAmount);
        Assert.Equal(200m, partialReturn.OutstandingAmount);
        Assert.Equal(PayrollPaymentResolutionStatuses.PendingInvestigation,
            partialReturn.ResolutionStatus);
        var returned = await creator.RecordPaymentEvidenceAsync(
            paymentId,
            new PayrollPaymentEvidenceRequest(
                partialReturn.Version, "return-posted-lineage-complete",
                new DateOnly(2026, 10, 21), "RETURN-REMAINDER-800",
                [
                    new PayrollPaymentAllocationEvidenceInput(1, 300m),
                    new PayrollPaymentAllocationEvidenceInput(2, 500m)
                ], "Remaining verified return"),
            PayrollPaymentEvidenceTypes.Return,
            new string('S', 64));
        Assert.Equal(PayrollPaymentStatuses.Returned, returned.Status);
        Assert.Equal(0m, returned.PaidAmount);
        Assert.Equal(1000m, returned.OutstandingAmount);
        Assert.Equal(PayrollPaymentResolutionStatuses.ResolvedReturn, returned.ResolutionStatus);

        var postingsAfterReturn = await verifyAfterCorrection.PayrollAccountingPostings
            .AsNoTracking().Where(row => row.PayrollRunId == runId)
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.PostingType, row.Status, row.PostingReference,
                row.RequestPayloadHash, row.AccountingJournalId, row.AccountingReference
            }).ToArrayAsync();
        var returnedJournalIds = postingsAfterReturn
            .Select(row => row.AccountingJournalId!.Value).Distinct().ToList();
        var journalsAfterReturn = await verifyAfterCorrection.JournalEntries
            .AsNoTracking().Where(row => returnedJournalIds.Contains(row.Id))
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id, row.ReferenceId, row.ReversalOfJournalEntryId,
                Lines = row.Lines.OrderBy(line => line.Id).Select(line => new
                {
                    line.Id, line.AccountId, line.DebitAmount, line.CreditAmount,
                    line.Description
                }).ToArray()
            }).ToArrayAsync();
        Assert.Equal(accountingLineageBeforeReturn,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                Postings = postingsAfterReturn,
                Journals = journalsAfterReturn
            }));
        var originalJournalAfterReturn = await verifyAfterCorrection.JournalEntries.AsNoTracking()
            .SingleAsync(row => row.Id == originalExpensePosting.AccountingJournalId);
        Assert.Equal(JournalEntryStatuses.Reversed, originalJournalAfterReturn.Status);
        Assert.Equal(correctionAccountingPosting.ReversalJournalId,
            originalJournalAfterReturn.ReversedByJournalEntryId);

        var returnedPeriod = await verifyAfterCorrection.PayrollPeriods.AsNoTracking()
            .SingleAsync(row => row.Id == runBeforeCorrection.PayrollPeriodId);
        var closePreview = await new PayrollPeriodStore(verifyAfterCorrection).PreviewCloseAsync(
            1, 101, returnedPeriod.Id,
            new ClosePayrollPeriodRequest(
                "returned-lineage-close-check", "Returned payment must block close",
                returnedPeriod.Version, 1, []),
            CancellationToken.None);
        Assert.False(closePreview.Response.Closed);
        Assert.Contains(closePreview.Response.Gates, gate =>
            gate.Code == "unpaid_partial_liability" && !gate.Passed);

        var reissued = await creator.RetryPaymentAsync(
            paymentId,
            new RetryPayrollPaymentRequest(
                returned.Version, "reissue-posted-lineage", "return-confirmed",
                [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, 1000m)]),
            new string('S', 64));
        Assert.NotEqual(paymentId, reissued.Id);
        Assert.Equal(paymentId, reissued.OriginalPaymentId);
        Assert.Equal(2, reissued.AttemptNo);
        Assert.Equal(PayrollPaymentStatuses.Ready, reissued.Status);
        long reissueAllocationId;
        long processingReissueVersion;
        await using (var reissueSource = CreateDb(database.ConnectionString))
        {
            var processingReissue = await reissueSource.PayrollPayments
                .SingleAsync(row => row.Id == reissued.Id);
            processingReissue.Status = PayrollPaymentStatuses.Processing;
            processingReissue.Version++;
            await reissueSource.SaveChangesAsync();
            processingReissueVersion = processingReissue.Version;
            reissueAllocationId = await reissueSource.PayrollPaymentAllocations
                .Where(row => row.PayrollPaymentId == reissued.Id)
                .Select(row => row.Id).SingleAsync();
        }
        await using var reissueWorker = new Session(database.ConnectionString);
        var reissueSettled = await reissueWorker.RecordPaymentEvidenceAsync(
            reissued.Id,
            new PayrollPaymentEvidenceRequest(
                processingReissueVersion, "reissue-settlement-evidence",
                new DateOnly(2026, 10, 21), "REISSUE-SETTLEMENT",
                [new PayrollPaymentAllocationEvidenceInput(1, 1000m)]),
            PayrollPaymentEvidenceTypes.Settlement,
            new string('T', 64));
        Assert.Equal(PayrollPaymentStatuses.Paid, reissueSettled.Status);
        Assert.Equal(1000m, reissueSettled.PaidAmount);
        Assert.Equal(0m, reissueSettled.OutstandingAmount);
        var reissueEvidence = Assert.Single(reissueSettled.Evidence,
            evidence => evidence.Type == PayrollPaymentEvidenceTypes.Settlement);
        var reissueReconciliation = await reissueWorker.ReconcilePaymentAsync(
            reissued.Id, reissueSettled.Version, expectedReconciliationVersion: 0,
            "reissue-lineage-reconciliation", new string('U', 64),
            "REISSUE-RECONCILED");
        Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled,
            Assert.Single(reissueReconciliation.Items).Status);
        PayrollAccountingSettlementSource settlementSourceSnapshot;
        await using (var reissueEligibilityDb = CreateDb(database.ConnectionString))
        {
            settlementSourceSnapshot = await new PayrollAccountingPostingStore(reissueEligibilityDb)
                .FindSettlementSourceAsync(1, reissued.Id, reissueAllocationId,
                    reissueEvidence.Id, CancellationToken.None)
                ?? throw new InvalidOperationException("Reissued settlement source was missing.");
        }
        var reissuePosting = await reissueWorker.CreatePayrollAccountingSettlementPostingService()
            .PostAsync(reissued.Id, reissueAllocationId, reissueEvidence.Id,
                CancellationToken.None);
        Assert.True(reissuePosting.Posting?.Status == PayrollAccountingPostingStatuses.Posted,
            $"{reissuePosting.Posting?.ErrorMessage ?? reissuePosting.Message} " +
            System.Text.Json.JsonSerializer.Serialize(settlementSourceSnapshot));
        Assert.NotNull(reissuePosting.Posting?.AccountingJournalId);
        Assert.StartsWith("payroll-settlement:1:", reissuePosting.Posting?.PostingReference);
        var reissuePostingReplay = await reissueWorker
            .CreatePayrollAccountingSettlementPostingService()
            .PostAsync(reissued.Id, reissueAllocationId, reissueEvidence.Id,
                CancellationToken.None);
        Assert.Equal(reissuePosting.Posting?.Id, reissuePostingReplay.Posting?.Id);
        Assert.Equal(reissuePosting.Posting?.AccountingJournalId,
            reissuePostingReplay.Posting?.AccountingJournalId);

        await using var verifyReissue = CreateDb(database.ConnectionString);
        var originalPaymentAfterReissue = await verifyReissue.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations).Include(row => row.Evidence)
            .SingleAsync(row => row.Id == paymentId);
        var reissuedPaymentAfterReissue = await verifyReissue.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations).Include(row => row.Evidence)
            .SingleAsync(row => row.Id == reissued.Id);
        Assert.Equal(PayrollPaymentStatuses.Returned, originalPaymentAfterReissue.Status);
        Assert.Equal(1000m, originalPaymentAfterReissue.OutstandingAmount);
        Assert.Equal(3, originalPaymentAfterReissue.Evidence.Count(
            evidence => evidence.Type == PayrollPaymentEvidenceTypes.Settlement));
        Assert.Equal(2, originalPaymentAfterReissue.Evidence.Count(
            evidence => evidence.Type == PayrollPaymentEvidenceTypes.Return));
        Assert.NotEqual(originalPaymentAfterReissue.Id, reissuedPaymentAfterReissue.Id);
        Assert.Equal(originalPaymentAfterReissue.Id, reissuedPaymentAfterReissue.OriginalPaymentId);
        Assert.Empty(originalPaymentAfterReissue.Allocations.Select(row => row.Id)
            .Intersect(reissuedPaymentAfterReissue.Allocations.Select(row => row.Id)));
        var reissuedPosting = await verifyReissue.PayrollAccountingPostings.AsNoTracking()
            .SingleAsync(row => row.PayrollPaymentId == reissued.Id
                && row.PostingType == PayrollAccountingPostingTypes.SalaryPayment);
        Assert.NotNull(reissuedPosting.AccountingJournalId);
        Assert.Equal(PayrollAccountingPostingStatuses.Posted, reissuedPosting.Status);
        Assert.DoesNotContain(postingBeforeCorrection,
            originalPosting => originalPosting.Id == reissuedPosting.Id);
        Assert.Equal(4, await verifyReissue.JournalEntries.CountAsync(entry =>
            entry.ReferenceId.StartsWith("payroll-settlement:1:")));
        Assert.Single(await verifyReissue.PayrollAccountingPostings.Where(row =>
            row.PayrollPaymentId == reissued.Id
            && row.PayrollPaymentEvidenceId == reissueEvidence.Id).ToArrayAsync());
        Assert.Equal(2, await verifyReissue.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.returned"
            && row.PrimaryEntityId == paymentId.ToString()));
        Assert.Single(await verifyReissue.AuditEvents.Where(row =>
            row.ActionType == "payroll.payment.retried"
            && row.PrimaryEntityId == paymentId.ToString()).ToArrayAsync());

        long competingCorrectionRunId;
        await using (var competingCorrection = new Session(database.ConnectionString))
        {
            await using var current = CreateDb(database.ConnectionString);
            var period = await current.PayrollPeriods.AsNoTracking()
                .SingleAsync(row => row.Id == runBeforeCorrection.PayrollPeriodId);
            var competingRun = await competingCorrection.CreateCorrectionAsync(
                runId, "CORR-POSTED-COMPETING", "correction-posted-competing",
                period.Version, runBeforeCorrection.Version);
            competingCorrectionRunId = competingRun.Id;
            await competingCorrection.CalculateAsync(
                runBeforeCorrection.PayrollPeriodId, competingRun.Id);
            var rejectedApproval = await Assert.ThrowsAsync<PayrollRunConflictException>(
                () => competingCorrection.SubmitAndApproveRunAsync(
                    competingRun.Id, "competing-corrected-run"));
            Assert.Contains("blocking validation findings", rejectedApproval.Message);
        }
        await using (var verifyCompetingApproval = CreateDb(database.ConnectionString))
        {
            var competingRun = await verifyCompetingApproval.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == competingCorrectionRunId);
            Assert.Equal(PayrollRunStatuses.Draft, competingRun.Status);
            Assert.Null(competingRun.ApprovalInstanceId);
            Assert.Empty(await verifyCompetingApproval.ApprovalInstances.AsNoTracking()
                .Where(row => row.TargetId == competingCorrectionRunId.ToString())
                .ToArrayAsync());
            Assert.False(await verifyCompetingApproval.PayrollAccountingPostings.AsNoTracking()
                .AnyAsync(row => row.PayrollRunId == competingCorrectionRunId
                    && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection));
            Assert.Empty(await verifyCompetingApproval.AuditEvents.AsNoTracking()
                .Where(row => row.ActionType == "payroll.accounting.correction_conflict"
                    && row.PrimaryEntityId == competingCorrectionRunId.ToString())
                .ToArrayAsync());
            Assert.Equal(3, await verifyCompetingApproval.PayrollRuns.CountAsync(row =>
                row.PayrollPeriodId == runBeforeCorrection.PayrollPeriodId));
            Assert.Equal(2, await verifyCompetingApproval.PayrollPeriodReopenEvents
                .CountAsync(row => row.OriginalPayrollRunId == runId));
        }
    }

    [Fact]
    public async Task ConcurrentRegularRunCreationHasOneDurableRunAndSelectionSet()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1,
                VersionNo = 1,
                EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT",
                DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101
            });
            setup.Users.Add(new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Payroll actor",
                Email = "payroll-run-postgres@example.invalid",
                PasswordHash = "test",
                IsActive = true
            });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                Status = PayrollPeriodStatuses.Processing, Version = 2,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
        }

        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollRunResponse? Run, Exception? Error)> Create(Session session, string key)
        {
            await gate.Task;
            try { return (await session.CreateAsync(key), null); }
            catch (Exception exception) { return (null, exception); }
        }

        var createOne = Create(first, "parallel-run-1");
        var createTwo = Create(second, "parallel-run-2");
        gate.SetResult();
        var results = await Task.WhenAll(createOne, createTwo);
        Assert.Single(results, result => result.Run is not null);
        Assert.Single(results, result => result.Error is PayrollRunConflictException);

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Single(await verify.PayrollRuns.ToArrayAsync());
        var selection = await verify.PayrollRunEmployeeSelections.SingleAsync();
        Assert.Equal(1, selection.EmployeeId);
        Assert.False(selection.IsIncluded);
        Assert.Equal("NoApprovedSalary", selection.ExclusionReasonCode);
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.run.created").ToArrayAsync());

        var calculationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayrollRunCalculationResponse> Calculate(Session session)
        {
            await calculationGate.Task;
            return await session.CalculateAsync();
        }
        var calculationOne = Calculate(first);
        var calculationTwo = Calculate(second);
        calculationGate.SetResult();
        var calculations = await Task.WhenAll(calculationOne, calculationTwo);
        Assert.Single(calculations, result => !result.IsReplay);
        Assert.Single(calculations, result => result.IsReplay);

        await using var verifyCalculation = CreateDb(database.ConnectionString);
        var calculatedRun = await verifyCalculation.PayrollRuns.SingleAsync();
        Assert.NotNull(calculatedRun.CalculatedAtUtc);
        Assert.Equal(2, calculatedRun.Version);
        Assert.Empty(await verifyCalculation.PayrollEmployeeResults.ToArrayAsync());
        Assert.Single(await verifyCalculation.AuditEvents.Where(row =>
            row.ActionType == "payroll.run.calculated").ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentPayrollRunApprovalCallbacksHaveOneTerminalWinner()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll approval concurrency" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            setup.Users.AddRange(
                new User { Id = 101, BusinessId = 1, Name = "Maker",
                    Email = "payroll-run-maker@example.invalid", PasswordHash = "test", IsActive = true },
                new User { Id = 202, BusinessId = 1, Name = "Approver",
                    Email = "payroll-run-approver@example.invalid", PasswordHash = "test", IsActive = true });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            setup.PayrollPeriods.Add(new PayrollPeriod
            {
                BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                Status = PayrollPeriodStatuses.Processing, Version = 2,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
            });
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
        }

        await using var maker = new Session(database.ConnectionString);
        var created = await maker.CreateAsync("approval-concurrency-run");
        Assert.Equal(1, created.IncludedEmployeeCount);
        await maker.CalculateAsync();
        var target = await maker.ReadApprovalTargetAsync(created.Id);
        Assert.NotNull(target);
        var approvalInstanceId = 90_001L;
        await using (var seed = CreateDb(database.ConnectionString))
        {
            var run = await seed.PayrollRuns.SingleAsync(row => row.Id == created.Id);
            run.Status = PayrollRunStatuses.PendingApproval;
            run.ApprovalInstanceId = approvalInstanceId;
            run.SubmittedByUserId = 101;
            run.SubmittedAtUtc = DateTime.UtcNow;
            run.SubmittedTargetVersion = run.Version;
            run.ApprovalEvidenceHash = target.Hash;
            run.Version++;
            await seed.SaveChangesAsync();
        }
        await using (var lockSession = new Session(database.ConnectionString))
            await lockSession.LockPeriodAsync(1, expectedVersion: 2);

        var approval = new ApprovalInstance
        {
            Id = approvalInstanceId,
            BusinessId = 1,
            ProcessType = ApprovalProcessType.PayrollRun,
            TargetType = PayrollRunApprovalConstants.TargetType,
            TargetId = created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TargetVersion = target.Version,
            RequestHash = target.Hash,
            Status = ApprovalInstanceStatus.Approved
        };
        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Decide(Session session, long actorUserId)
        {
            await gate.Task;
            try
            {
                await session.DecidePayrollRunAsync(approval, actorUserId);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var decisionOne = Decide(first, 202);
        var decisionTwo = Decide(second, 202);
        gate.SetResult();
        var outcomes = await Task.WhenAll(decisionOne, decisionTwo);
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is ApprovalKernelConflictException);

        await using var verify = CreateDb(database.ConnectionString);
        var approved = await verify.PayrollRuns.SingleAsync(row => row.Id == created.Id);
        Assert.Equal(PayrollRunStatuses.Approved, approved.Status);
        Assert.Equal(202, approved.DecidedByUserId);
        Assert.Equal(4, approved.Version);
        Assert.Equal(target.Hash, approved.ApprovalEvidenceHash);
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "hr.approval.decided").ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCorrectionRequestsForOnePeriodHaveOneWinner()
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
        long originalRunId;
        await using (var setup = CreateDb(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll correction concurrency" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            setup.Users.Add(new User
            {
                Id = 101, BusinessId = 1, Name = "Correction maker",
                Email = "payroll-correction-maker@example.invalid",
                PasswordHash = "test", IsActive = true
            });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            var period = new PayrollPeriod
            {
                Id = 1, BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                Status = PayrollPeriodStatuses.Approved, Version = 4,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101
            };
            setup.PayrollPeriods.Add(period);
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
            var salaryVersion = await setup.PayrollEmployeeSalaryVersions
                .SingleAsync(row => row.EmployeeId == 1);
            var monetary = await setup.PayrollMonetaryConfigurationVersions
                .SingleAsync(row => row.BusinessId == 1);
            var original = new PayrollRun
            {
                Id = 1, BusinessId = 1, PayrollPeriodId = period.Id, RunNo = 1,
                RunType = PayrollRunTypes.Regular, ScopeType = PayrollRunScopeTypes.Business,
                ScopeKey = PayrollRunScopeTypes.BusinessKey, Status = PayrollRunStatuses.Approved,
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                MonetaryConfigurationVersionId = monetary.Id, Version = 4,
                CalculationVersion = 1, CalculationInputHash = new string('D', 64),
                IdempotencyKey = "approved-source-run",
                IdempotencyPayloadHash = new string('E', 64),
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
            };
            setup.PayrollRuns.Add(original);
            await setup.SaveChangesAsync();
            setup.PayrollRunEmployeeSelections.Add(new PayrollRunEmployeeSelection
            {
                BusinessId = 1, PayrollRunId = original.Id, EmployeeId = 1,
                EmployeeCodeSnapshot = "EMP-1", EmployeeNameSnapshot = "Employee One",
                LocationIdSnapshot = 10, EligibilityDate = new(2026, 10, 1),
                SalaryVersionId = salaryVersion.Id, SalaryEffectiveFromSnapshot = new(2026, 1, 1),
                IsIncluded = true, SelectedAtUtc = DateTime.UtcNow
            });
            setup.PayrollEmployeeResults.Add(new PayrollEmployeeResult
            {
                BusinessId = 1, PayrollRunId = original.Id, CalculationVersion = 1,
                CalculationInputHash = new string('D', 64),
                CalculationSnapshotJson = "{}", CalculationSnapshotHash = new string('F', 64),
                EmployeeId = 1, EmployeeCodeSnapshot = "EMP-1", EmployeeNameSnapshot = "Employee One",
                LocationIdSnapshot = 10, SalaryVersionId = salaryVersion.Id,
                SalaryEffectiveFromSnapshot = new(2026, 1, 1),
                GrossEarnings = 1000m, TotalDeductions = 0m, NetPay = 1000m,
                Status = PayrollEmployeeResultStatuses.Calculated, CalculatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
            originalRunId = original.Id;
        }

        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PayrollRunResponse? Run, Exception? Error)> CreateCorrection(
            Session session, string reference, string key)
        {
            await gate.Task;
            try { return (await session.CreateCorrectionAsync(
                originalRunId, reference, key, expectedPeriodVersion: 4), null); }
            catch (Exception exception) { return (null, exception); }
        }

        var createOne = CreateCorrection(first, "CORR-PG-1", "correction-pg-1");
        var createTwo = CreateCorrection(second, "CORR-PG-2", "correction-pg-2");
        gate.SetResult();
        var results = await Task.WhenAll(createOne, createTwo);
        Assert.Single(results, result => result.Run is not null);
        Assert.Single(results, result => result.Error is PayrollRunConflictException);

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Equal(2, await verify.PayrollRuns.CountAsync());
        var correction = await verify.PayrollRuns.SingleAsync(row =>
            row.RunType == PayrollRunTypes.Correction);
        Assert.Equal(originalRunId, correction.OriginalPayrollRunId);
        Assert.Single(await verify.PayrollPeriodReopenEvents.ToArrayAsync());
        Assert.Equal(PayrollPeriodStatuses.Processing,
            (await verify.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
    }

    [Fact]
    public async Task ConcurrentPaymentAttemptsCreateOneAttemptAndSameKeyReplays()
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
        await using (var setup = CreateDb(database.ConnectionString))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            setup.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll payments" },
                new Business { Id = 2, Name = "Other payroll" });
            setup.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1, VersionNo = 1, EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101
            });
            setup.Users.AddRange(
                new User
                {
                    Id = 101, BusinessId = 1, Name = "Payment maker",
                    Email = "payroll-payment-maker@example.invalid",
                    PasswordHash = "test", IsActive = true
                },
                new User
                {
                    Id = 202, BusinessId = 1, Name = "Approver",
                    Email = "payroll-payment-approver@example.invalid",
                    PasswordHash = "test", IsActive = true
                });
            await setup.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(setup, businessExists: true);
            var period = new PayrollPeriod
            {
                Id = 1, BusinessId = 1, PeriodCode = "2026-10",
                PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
                Status = PayrollPeriodStatuses.Approved, Version = 4,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
                OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
                ApprovedAtUtc = DateTime.UtcNow, ApprovedByUserId = 202
            };
            setup.PayrollPeriods.Add(period);
            await setup.SaveChangesAsync();
            await SeedApprovedSalaryAsync(setup);
            var salaryVersion = await setup.PayrollEmployeeSalaryVersions
                .SingleAsync(row => row.EmployeeId == 1);
            var monetary = await setup.PayrollMonetaryConfigurationVersions
                .SingleAsync(row => row.BusinessId == 1);
            var run = new PayrollRun
            {
                Id = 1, BusinessId = 1, PayrollPeriodId = period.Id, RunNo = 1,
                RunType = PayrollRunTypes.Regular, ScopeType = PayrollRunScopeTypes.Business,
                ScopeKey = PayrollRunScopeTypes.BusinessKey, Status = PayrollRunStatuses.Approved,
                CurrencyCode = "BDT", DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                MonetaryConfigurationVersionId = monetary.Id, Version = 5,
                CalculationVersion = 1, CalculationInputHash = new string('D', 64),
                IdempotencyKey = "approved-payment-run",
                IdempotencyPayloadHash = new string('E', 64),
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow,
                DecidedByUserId = 202, DecidedAtUtc = DateTime.UtcNow
            };
            setup.PayrollRuns.Add(run);
            await setup.SaveChangesAsync();
            setup.PayrollEmployeeResults.Add(new PayrollEmployeeResult
            {
                BusinessId = 1, PayrollRunId = run.Id, CalculationVersion = 1,
                CalculationInputHash = new string('D', 64),
                CalculationSnapshotJson = """{"netPay":1000}""",
                CalculationSnapshotHash = new string('F', 64),
                EmployeeId = 1, EmployeeCodeSnapshot = "EMP-1",
                EmployeeNameSnapshot = "Employee One", LocationIdSnapshot = 10,
                SalaryVersionId = salaryVersion.Id,
                SalaryEffectiveFromSnapshot = new(2026, 1, 1),
                GrossEarnings = 1000m, TotalDeductions = 0m, NetPay = 1000m,
                Status = PayrollEmployeeResultStatuses.Calculated,
                CalculatedAtUtc = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using var first = new Session(database.ConnectionString);
        await using var second = new Session(database.ConnectionString);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(string Key, PayrollPaymentResponse? Payment, Exception? Error)> Create(
            Session session, string key)
        {
            await gate.Task;
            try { return (key, await session.CreatePaymentAsync(key), null); }
            catch (Exception exception) { return (key, null, exception); }
        }

        var createOne = Create(first, "payment-concurrent-1");
        var createTwo = Create(second, "payment-concurrent-2");
        gate.SetResult();
        var results = await Task.WhenAll(createOne, createTwo);
        var winner = Assert.Single(results, result => result.Payment is not null);
        Assert.Single(results, result => result.Error is PayrollPaymentConflictException);

        await using var replaySession = new Session(database.ConnectionString);
        var replay = await replaySession.CreatePaymentAsync(winner.Key);
        Assert.True(replay.IsReplay);
        Assert.Equal(winner.Payment!.Id, replay.Id);
        await Assert.ThrowsAsync<PayrollPaymentConflictException>(
            () => replaySession.CreatePaymentAsync(winner.Key, payloadHash: new string('B', 64)));

        await using var verify = CreateDb(database.ConnectionString);
        Assert.Single(await verify.PayrollPayments.ToArrayAsync());
        Assert.Single(await verify.PayrollPaymentAllocations.ToArrayAsync());
        Assert.Single(await verify.AuditEvents.Where(row =>
            row.ActionType == "payroll.payment.created").ToArrayAsync());

        await using var batchCreator = new Session(database.ConnectionString);
        var batch = await batchCreator.CreateBatchAsync(winner.Payment.Id);
        await using var submitOne = new Session(database.ConnectionString);
        await using var submitTwo = new Session(database.ConnectionString);
        var submitGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(string Key, PayrollPaymentBatchResponse? Batch, Exception? Error)> Submit(
            Session session, string key)
        {
            await submitGate.Task;
            try
            {
                return (key, await session.SubmitBatchAsync(batch.Id, batch.Version, key), null);
            }
            catch (Exception exception)
            {
                return (key, null, exception);
            }
        }

        var submitFirst = Submit(submitOne, "batch-submit-concurrent-1");
        var submitSecond = Submit(submitTwo, "batch-submit-concurrent-2");
        submitGate.SetResult();
        var submissions = await Task.WhenAll(submitFirst, submitSecond);
        var submissionWinner = Assert.Single(
            submissions, result => result.Batch is not null);
        Assert.Single(submissions, result =>
            result.Error is PayrollPaymentConflictException);

        await using var submissionReplay = new Session(database.ConnectionString);
        var replayedBatch = await submissionReplay.SubmitBatchAsync(
            batch.Id, batch.Version, submissionWinner.Key);
        Assert.True(replayedBatch.IsReplay);
        Assert.Equal(submissionWinner.Batch!.SubmittedByUserId, replayedBatch.SubmittedByUserId);
        await Assert.ThrowsAsync<PayrollPaymentConflictException>(
            () => submissionReplay.SubmitBatchAsync(
                batch.Id, batch.Version, submissionWinner.Key, externalReference: "changed"));

        await using var verifyBatch = CreateDb(database.ConnectionString);
        var persistedBatch = await verifyBatch.PayrollPaymentBatches
            .SingleAsync(row => row.Id == batch.Id);
        Assert.Equal(PayrollPaymentBatchStatuses.Processing, persistedBatch.Status);
        Assert.Equal(submissionWinner.Key, persistedBatch.SubmissionIdempotencyKey);
        Assert.Equal(PayrollPaymentStatuses.Processing,
            (await verifyBatch.PayrollPayments.SingleAsync(row =>
                row.Id == winner.Payment.Id)).Status);
        Assert.Equal(2, await verifyBatch.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment_batch.submitted"));

        long settledPaymentVersion;
        await using (var settlement = CreateDb(database.ConnectionString))
        {
            var payment = await settlement.PayrollPayments
                .SingleAsync(row => row.Id == winner.Payment.Id);
            var allocation = await settlement.PayrollPaymentAllocations
                .SingleAsync(row => row.PayrollPaymentId == payment.Id);
            payment.Status = PayrollPaymentStatuses.Paid;
            payment.PaidAmount = 1000m;
            payment.OutstandingAmount = 0m;
            payment.Version++;
            allocation.SettledAmount = 1000m;
            allocation.State = PayrollPaymentAllocationStates.Settled;
            await settlement.SaveChangesAsync();
            settledPaymentVersion = payment.Version;
        }

        await using var reconcileOne = new Session(database.ConnectionString);
        await using var reconcileTwo = new Session(database.ConnectionString);
        var reconcileGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(string Key, PayrollPaymentReconciliationOperationResponse? Result, Exception? Error)>
            Reconcile(Session session, string key, string reference)
        {
            await reconcileGate.Task;
            try
            {
                return (key, await session.ReconcilePaymentAsync(
                    winner.Payment.Id, settledPaymentVersion, 0, key,
                    new string('1', 64), reference), null);
            }
            catch (Exception exception) { return (key, null, exception); }
        }

        var reconcileFirst = Reconcile(reconcileOne,
            "payment-reconcile-concurrent-1", "reconcile-reference-1");
        var reconcileSecond = Reconcile(reconcileTwo,
            "payment-reconcile-concurrent-2", "reconcile-reference-2");
        reconcileGate.SetResult();
        var reconciliationResults = await Task.WhenAll(reconcileFirst, reconcileSecond);
        var reconciliationWinner = Assert.Single(
            reconciliationResults, result => result.Result is not null);
        Assert.Single(reconciliationResults,
            result => result.Error is PayrollPaymentConflictException);

        await using var reconciliationReplay = new Session(database.ConnectionString);
        var winningReconciliation = reconciliationWinner.Result!;
        var replayedReconciliation = await reconciliationReplay.ReconcilePaymentAsync(
            winner.Payment.Id, settledPaymentVersion, 0, reconciliationWinner.Key,
            new string('1', 64),
            winningReconciliation.Items.Single().ExternalReference!);
        Assert.True(replayedReconciliation.IsReplay);

        await using var verifyReconciliation = CreateDb(database.ConnectionString);
        var persistedReconciliation = await verifyReconciliation.PayrollPaymentReconciliations
            .SingleAsync();
        Assert.Equal(1, persistedReconciliation.Version);
        Assert.Equal(1, await verifyReconciliation.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.reconciled"));
    }

    private static async Task SeedApprovedSalaryAsync(RetailPOSDbContext db)
    {
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1, Code = "BASE_APPROVAL_CONCURRENCY", Name = "Base salary",
            ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
        };
        var structure = new PayrollSalaryStructure
        {
            BusinessId = 1, Code = "STRUCT-APPROVAL-CONCURRENCY", Name = "Standard"
        };
        var policy = new ApprovalPolicy
        {
            BusinessId = 1, Code = "SALARY-APPROVAL-CONCURRENCY",
            ProcessType = ApprovalProcessType.SalarySetup, Name = "Salary setup",
            VersionNo = 1, ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2020, 1, 1), IsActive = true
        };
        db.PayrollSalaryComponents.Add(component);
        db.PayrollSalaryStructures.Add(structure);
        db.ApprovalPolicies.Add(policy);
        await db.SaveChangesAsync();
        var approval = new ApprovalInstance
        {
            BusinessId = 1, PolicyId = policy.Id, PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = policy.VersionNo, ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = "PayrollSalarySetupRequest", TargetId = "1",
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business, TargetVersion = 1,
            RequestHash = new string('A', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();
        var componentSnapshot = System.Text.Json.JsonSerializer.Serialize(
            new[]
            {
                new PayrollSalarySetupComponentSnapshot(
                    component.Id, component.Code, component.Name, component.ComponentType,
                    component.ValueType, null, true, 1, null, 1000m)
            },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var request = new PayrollSalarySetupRequest
        {
            BusinessId = 1, EmployeeId = 1, SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1, EffectiveFrom = new(2026, 1, 1),
            OperationalLocationProjectionId = 10, StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot, RequestHash = new string('B', 64),
            IdempotencyPayloadHash = new string('C', 64), IdempotencyKey = "approved-salary",
            Reason = "Concurrency test setup", Status = "Approved", ApprovalInstanceId = approval.Id,
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow,
            DecidedByUserId = 202, DecidedAtUtc = DateTime.UtcNow, Version = 2
        };
        db.PayrollSalarySetupRequests.Add(request);
        await db.SaveChangesAsync();
        db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
        {
            BusinessId = 1, EmployeeId = 1, SalarySetupRequestId = request.Id,
            SalaryStructureId = structure.Id, SalaryStructureVersion = 1,
            EffectiveFrom = new(2026, 1, 1), OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}", ComponentSnapshotJson = componentSnapshot,
            ApprovalInstanceId = approval.Id, ApprovedByUserId = 202,
            ApprovedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCrossArtifactSalaryAsync(RetailPOSDbContext db)
    {
        var components = new[]
        {
            new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "BASE_ACCEPTANCE", Name = "Base salary",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
            },
            new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "ALLOWANCE_ACCEPTANCE", Name = "Allowance",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
            },
            new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "OVERTIME_ACCEPTANCE", Name = "Overtime",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
            },
            new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "DEDUCTION_ACCEPTANCE", Name = "Deduction",
                ComponentType = "Deduction", ValueType = "Fixed", IsRecurring = true
            }
        };
        var structure = new PayrollSalaryStructure
        {
            BusinessId = 1, Code = "STRUCT-CROSS-ARTIFACT", Name = "Cross-artifact"
        };
        var policy = new ApprovalPolicy
        {
            BusinessId = 1, Code = "SALARY-CROSS-ARTIFACT",
            ProcessType = ApprovalProcessType.SalarySetup, Name = "Salary setup",
            VersionNo = 1, ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2020, 1, 1), IsActive = true
        };
        db.PayrollSalaryComponents.AddRange(components);
        db.PayrollSalaryStructures.Add(structure);
        db.ApprovalPolicies.Add(policy);
        await db.SaveChangesAsync();
        var approval = new ApprovalInstance
        {
            BusinessId = 1, PolicyId = policy.Id, PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = policy.VersionNo, ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = "PayrollSalarySetupRequest", TargetId = "1",
            RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business, TargetVersion = 1,
            RequestHash = new string('A', 64)
        };
        db.ApprovalInstances.Add(approval);
        await db.SaveChangesAsync();
        var amounts = new[] { 850m, 200m, 50m, 100m };
        var componentSnapshot = System.Text.Json.JsonSerializer.Serialize(
            components.Select((component, index) => new PayrollSalarySetupComponentSnapshot(
                component.Id, component.Code, component.Name, component.ComponentType,
                component.ValueType, null, true, index + 1, null, amounts[index])),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var request = new PayrollSalarySetupRequest
        {
            BusinessId = 1, EmployeeId = 1, SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1, EffectiveFrom = new(2026, 1, 1),
            OperationalLocationProjectionId = 10, StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot, RequestHash = new string('B', 64),
            IdempotencyPayloadHash = new string('C', 64), IdempotencyKey = "cross-artifact-salary",
            Reason = "Cross-artifact acceptance fixture", Status = "Approved",
            ApprovalInstanceId = approval.Id, CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow, DecidedByUserId = 202,
            DecidedAtUtc = DateTime.UtcNow, Version = 2
        };
        db.PayrollSalarySetupRequests.Add(request);
        await db.SaveChangesAsync();
        db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
        {
            BusinessId = 1, EmployeeId = 1, SalarySetupRequestId = request.Id,
            SalaryStructureId = structure.Id, SalaryStructureVersion = 1,
            EffectiveFrom = new(2026, 1, 1), OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}", ComponentSnapshotJson = componentSnapshot,
            ApprovalInstanceId = approval.Id, ApprovedByUserId = 202,
            ApprovedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<byte[]> ReadDownloadAsync(PayrollExportDownload download)
    {
        await using var content = download.Content;
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static string GetPostgresConnectionString()
    {
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        return connectionString!;
    }

    private static async Task MarkExpensePostingConflictAsync(
        string connectionString, long postingId)
    {
        await using var db = CreateDb(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({1L ^ (long.MinValue + 13)})");
        var periodStatus = await db.PayrollPeriods.AsNoTracking()
            .Where(row => row.Id == 1 && row.BusinessId == 1)
            .Select(row => row.Status)
            .SingleAsync();
        if (periodStatus == PayrollPeriodStatuses.Closed)
            throw new PayrollPeriodConflictException(
                "Accounting posting mutation is prohibited for a closed Payroll period.");
        var posting = await db.PayrollAccountingPostings.SingleAsync(
            row => row.BusinessId == 1 && row.Id == postingId);
        posting.Status = PayrollAccountingPostingStatuses.Conflict;
        posting.AccountingJournalId = null;
        posting.AccountingReference = null;
        posting.PostedAtUtc = null;
        posting.ErrorMessage = "Test-only close race conflict.";
        posting.Version++;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static RetailPOSDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private sealed class PayslipSession : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        private readonly IRequiredAuditTransaction audit;
        private readonly PayrollPayslipStore payslips;

        public PayslipSession(string connectionString)
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/runs/1/payslips/1", null, null, null,
                false, null, null, null, null, "actor", "Payroll"));
            db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(new AuditChangeTrackerInterceptor(auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance))
                .Options);
            audit = new RequiredAuditTransaction(db, auditContext,
                new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                    NullLogger<AuditService>.Instance),
                NullLogger<RequiredAuditTransaction>.Instance);
            payslips = new PayrollPayslipStore(db);
        }

        public Task<PayrollPayslipArtifactWriteResult> CreateArtifactAsync(
            PayrollPayslipArtifactCandidate candidate) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(candidate.BusinessId, "payroll.payslip.generated",
                    "Payroll", "Generated a confidential Payslip artifact.", "Payslip",
                    $"{candidate.RunId}:{candidate.EmployeeResultId}"),
                token => payslips.CreateOrReuseAsync(candidate, token),
                result => !result.IsReplay, CancellationToken.None);

        public Task<PayrollPayslipDeliveryWriteResult> AddDeliveryAsync(
            PayrollPayslipDeliveryWrite delivery) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(delivery.BusinessId, "payroll.payslip.email_requested",
                    "Payroll", "Requested delivery of a confidential Payslip.",
                    "Payslip", delivery.ArtifactVersionId.ToString()),
                token => payslips.AddDeliveryAsync(delivery, token),
                result => !result.IsReplay, CancellationToken.None);

        public async Task<RetailPOS.Core.Entities.HR.Payroll.PayrollArtifactVersion> GetArtifactAsync(
            long businessId, Guid artifactId) =>
            await payslips.FindAsync(businessId, artifactId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Payslip artifact was not persisted.");

        public PayrollPayslipService CreateGenerationService(
            MemoryPayslipBlobStore blobs,
            IPayrollPayslipRenderer? renderer = null,
            string scopeType = PayrollRunScopeTypes.Location,
            long? scopeId = 10)
        {
            var access = CreateAccess(scopeType, scopeId);
            return new PayrollPayslipService(
                access.Object, payslips, renderer ?? new TestPayslipRenderer(), blobs,
                new TestPayslipEncryption(), new TestPayslipInspector(),
                Mock.Of<IPayrollPayslipEmailTransport>(), audit, TimeProvider.System,
                Mock.Of<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(settings =>
                    settings.GetAsync(It.IsAny<CancellationToken>()) ==
                    Task.FromResult(new RetailPOS.Application.Settings.SystemSettings())));
        }

        public PayrollPayslipService CreateEmailService(
            IPayrollPayslipEmailTransport transport,
            TaskCompletionSource? replayObserved = null)
        {
            return new PayrollPayslipService(
                CreateAccess().Object,
                replayObserved is null ? payslips : new ObservedPayslipStore(payslips, replayObserved),
                Mock.Of<IPayrollPayslipRenderer>(),
                Mock.Of<RetailPOS.Application.ConfidentialFiles.IPrivateBlobStore>(),
                Mock.Of<RetailPOS.Application.ConfidentialFiles.IConfidentialFileEncryption>(),
                Mock.Of<RetailPOS.Application.ConfidentialFiles.IConfidentialContentInspector>(),
                transport, audit, TimeProvider.System,
                Mock.Of<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(settings =>
                    settings.GetAsync(It.IsAny<CancellationToken>()) ==
                    Task.FromResult(new RetailPOS.Application.Settings.SystemSettings())));
        }

        private static Mock<IPayrollRunAccess> CreateAccess(
            string scopeType = PayrollRunScopeTypes.Location,
            long? scopeId = 10)
        {
            var access = new Mock<IPayrollRunAccess>();
            access.Setup(item => item.RequireBusinessId()).Returns(1);
            access.Setup(item => item.RequireRealUserId()).Returns(101);
            access.Setup(item => item.EnsurePermissionAsync(
                1, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            access.Setup(item => item.EnsureScopeAsync(
                1, scopeType, scopeId, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return access;
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class ObservedPayslipStore(
        IPayrollPayslipStore inner, TaskCompletionSource replayObserved) : IPayrollPayslipStore
    {
        public Task<PayrollPayslipSource> GetSourceAsync(
            long businessId, long runId, long employeeId, CancellationToken cancellationToken) =>
            inner.GetSourceAsync(businessId, runId, employeeId, cancellationToken);

        public Task<RetailPOS.Core.Entities.HR.Payroll.PayrollArtifactVersion?> FindLatestAsync(
            long businessId, long runId, long employeeId, CancellationToken cancellationToken) =>
            inner.FindLatestAsync(businessId, runId, employeeId, cancellationToken);

        public Task<RetailPOS.Core.Entities.HR.Payroll.PayrollArtifactVersion?> FindAsync(
            long businessId, Guid artifactVersionId, CancellationToken cancellationToken) =>
            inner.FindAsync(businessId, artifactVersionId, cancellationToken);

        public Task<PayrollPayslipArtifactWriteResult> CreateOrReuseAsync(
            PayrollPayslipArtifactCandidate candidate, CancellationToken cancellationToken) =>
            inner.CreateOrReuseAsync(candidate, cancellationToken);

        public async Task<PayrollPayslipDeliveryWriteResult> AddDeliveryAsync(
            PayrollPayslipDeliveryWrite delivery, CancellationToken cancellationToken)
        {
            var result = await inner.AddDeliveryAsync(delivery, cancellationToken);
            if (result.IsReplay)
                replayObserved.TrySetResult();
            return result;
        }

        public Task<RetailPOS.Core.Entities.HR.Payroll.PayrollPayslipDelivery?> FindDeliveryAsync(
            long businessId, long actorUserId, string idempotencyKey,
            CancellationToken cancellationToken) =>
            inner.FindDeliveryAsync(businessId, actorUserId, idempotencyKey, cancellationToken);

        public Task<RetailPOS.Core.Entities.HR.Payroll.PayrollPayslipDelivery> UpdateDeliveryAsync(
            long deliveryId, long businessId, string status, string? providerReference,
            string? failureCode, DateTime? sentAtUtc, CancellationToken cancellationToken) =>
            inner.UpdateDeliveryAsync(deliveryId, businessId, status, providerReference,
                failureCode, sentAtUtc, cancellationToken);
    }

    private sealed class MemoryPayslipBlobStore : RetailPOS.Application.ConfidentialFiles.IPrivateBlobStore
    {
        private readonly object sync = new();
        private readonly Dictionary<string, byte[]> blobs = [];
        public int Count { get { lock (sync) return blobs.Count; } }
        public int DeletedCount { get; private set; }
        public bool Contains(string key) { lock (sync) return blobs.ContainsKey(key); }

        public Task<string> StageAsync(
            long businessId, ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken)
        {
            var key = $"payslip-stage-{Guid.NewGuid():N}";
            lock (sync) blobs.Add(key, ciphertext.ToArray());
            return Task.FromResult(key);
        }

        public Task<Stream> OpenAsync(long businessId, string key, CancellationToken cancellationToken)
        {
            lock (sync)
                return Task.FromResult<Stream>(new MemoryStream(blobs[key].ToArray(), writable: false));
        }

        public Task DeleteOwnedStageAsync(
            long businessId, string key, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (blobs.Remove(key, out var bytes))
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
                DeletedCount++;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class TestPayslipRenderer : IPayrollPayslipRenderer
    {
        public byte[] Render(PayrollPayslipSource source, string templateVersion) =>
            "%PDF-test"u8.ToArray();
    }

    private sealed class TestPayslipEncryption : RetailPOS.Application.ConfidentialFiles.IConfidentialFileEncryption
    {
        public Task<RetailPOS.Application.ConfidentialFiles.EncryptedConfidentialContent> EncryptAsync(
            ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken) =>
            Task.FromResult(new RetailPOS.Application.ConfidentialFiles.EncryptedConfidentialContent(
                plaintext.ToArray(), "test-key"));

        public Task<byte[]> DecryptAsync(
            ReadOnlyMemory<byte> ciphertext, string keyReference, CancellationToken cancellationToken) =>
            Task.FromResult(ciphertext.ToArray());
    }

    private sealed class TestPayslipInspector : RetailPOS.Application.ConfidentialFiles.IConfidentialContentInspector
    {
        public Task<RetailPOS.Application.ConfidentialFiles.ConfidentialContentValidation> ValidateAsync(
            ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new RetailPOS.Application.ConfidentialFiles.ConfidentialContentValidation(
                "application/pdf", ".pdf"));

        public Task<RetailPOS.Application.ConfidentialFiles.ConfidentialFileScanStatus> ScanAsync(
            ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            Task.FromResult(RetailPOS.Application.ConfidentialFiles.ConfidentialFileScanStatus.Clean);
    }

    private sealed class GatedPayslipEmailTransport(bool succeeds) : IPayrollPayslipEmailTransport
    {
        private int invocationCount;
        public int InvocationCount => Volatile.Read(ref invocationCount);
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        public async Task<PayrollPayslipEmailResult> DeliverAsync(
            long businessId, long employeeId, string recipient, Guid artifactVersionId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocationCount);
            Entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            if (!succeeds)
                throw new PayrollPayslipEmailTransportException("ProviderUnavailable");
            return new PayrollPayslipEmailResult(true, "provider-1");
        }
    }

    private sealed class ResolvedPayrollAccountingCorrectionPolicy
        : IPayrollAccountingCorrectionPolicy
    {
        public Task<PayrollAccountingCorrectionPolicy> ResolveAsync(
            long businessId, string correctionReference, CancellationToken cancellationToken) =>
            Task.FromResult(new PayrollAccountingCorrectionPolicy(
                PayrollAccountingCorrectionStrategy.ReversalAndReplacement,
                "TEST-ADR-06-ReversalAndReplacement-v1"));
    }

    private sealed class PayrollAccountingPostgresApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
                services.RemoveAll<RetailPOSDbContext>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
                services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
                    options.UseNpgsql(connectionString)
                        .AddInterceptors(
                            serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>()));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

                services.RemoveAll<IPayrollAccountingCorrectionPolicy>();
                services.AddSingleton<IPayrollAccountingCorrectionPolicy>(
                    new ResolvedPayrollAccountingCorrectionPolicy());
            });
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly RetailPOSDbContext db;
        private readonly IRequiredAuditTransaction audit;
        private readonly AuditContext auditContext;
        private readonly AuditService auditService;
        private readonly CapturingLogger<PayrollAccountingPostingService> postingLogger = new();
        private readonly PayrollRunStore runs;
        private readonly PayrollRunCalculationStore calculations;
        private readonly PayrollPeriodStore periods;
        private readonly PayrollPaymentStore payments;
        private readonly PayrollPaymentBatchStore batches;
        private readonly PayrollPaymentReconciliationStore reconciliations;

        public Session(
            string connectionString,
            RoleAuditFailureInterceptor? auditFailureInterceptor = null)
        {
            auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
                AuditSource.API, "POST", "/api/payroll/v1/periods/1/runs", null, null, null,
                false, null, null, null, null, "actor", "Payroll"));
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(connectionString);
            options.AddInterceptors(new AuditChangeTrackerInterceptor(auditContext,
                NullLogger<AuditChangeTrackerInterceptor>.Instance));
            if (auditFailureInterceptor is not null)
                options.AddInterceptors(auditFailureInterceptor);
            db = new RetailPOSDbContext(options.Options);
            auditService = new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                NullLogger<AuditService>.Instance);
            audit = new RequiredAuditTransaction(db, auditContext, auditService,
                NullLogger<RequiredAuditTransaction>.Instance);
            runs = new PayrollRunStore(db, new PayrollRunValidationStore(db),
                new PayrollRunCalculationStore(db));
            calculations = new PayrollRunCalculationStore(db);
            periods = new PayrollPeriodStore(db);
            payments = new PayrollPaymentStore(db);
            batches = new PayrollPaymentBatchStore(db);
            reconciliations = new PayrollPaymentReconciliationStore(db);
        }

        public Exception? PostingException => postingLogger.LastException;

        public PayrollAccountingPostingService CreatePayrollAccountingPostingService(
            IPayrollAccountingCorrectionPolicy? correctionPolicy = null)
        {
            var access = new Mock<IPayrollRunAccess>(MockBehavior.Strict);
            access.Setup(value => value.RequireBusinessId()).Returns(1);
            access.Setup(value => value.RequireRealUserId()).Returns(101);
            access.Setup(value => value.EnsurePermissionAsync(
                    1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(value => value.EnsureScopeAsync(
                    1, It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var capabilities = new Mock<IHrPayrollCapabilityService>(MockBehavior.Strict);
            capabilities.Setup(value => value.EnsureEnabledAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var legacy = new ManualJournalDraftStore(
                db, auditService, new JournalNumberAllocator(db),
                NullLogger<ManualJournalDraftStore>.Instance);
            var frozen = new FrozenSystemJournalStore(db, legacy, audit, auditContext);
            return new PayrollAccountingPostingService(
                access.Object, capabilities.Object, new PayrollAccountingPostingStore(db),
                new AccountingPostingAccountReadStore(db), frozen,
                new JournalEntryReadStore(db), audit,
                correctionPolicy ?? new UnconfiguredPayrollAccountingCorrectionPolicy(),
                postingLogger);
        }

        public PayrollAccountingSettlementPostingService CreatePayrollAccountingSettlementPostingService()
        {
            var access = new Mock<IPayrollRunAccess>(MockBehavior.Strict);
            access.Setup(value => value.RequireBusinessId()).Returns(1);
            access.Setup(value => value.RequireRealUserId()).Returns(101);
            access.Setup(value => value.EnsurePermissionAsync(
                    1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(value => value.EnsureScopeAsync(
                    1, It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var capabilities = new Mock<IHrPayrollCapabilityService>(MockBehavior.Strict);
            capabilities.Setup(value => value.EnsureEnabledAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var legacy = new ManualJournalDraftStore(
                db, auditService, new JournalNumberAllocator(db),
                NullLogger<ManualJournalDraftStore>.Instance);
            var frozen = new FrozenSystemJournalStore(db, legacy, audit, auditContext);
            return new PayrollAccountingSettlementPostingService(
                access.Object, capabilities.Object, new PayrollAccountingPostingStore(db),
                new AccountingPostingAccountReadStore(db), frozen,
                new JournalEntryReadStore(db), audit,
                NullLogger<PayrollAccountingSettlementPostingService>.Instance);
        }

        public Task<bool> TryMarkPeriodPostedAsync(long periodId) =>
            new PayrollAccountingPostingStore(db).TryMarkPeriodPostedAsync(
                1, periodId, 101, CancellationToken.None);

        public async Task<long> CommitCorrectionReversalAsync(
            long correctionRunId, long originalPostingId, long originalJournalId,
            string correctionReference)
        {
            var source = await new PayrollAccountingPostingStore(db).FindSourceAsync(
                1, correctionRunId, CancellationToken.None)
                ?? throw new InvalidOperationException("Correction run not found in test setup.");
            var date = new DateTime(source.PeriodEnd.Year, source.PeriodEnd.Month,
                source.PeriodEnd.Day, 0, 0, 0, DateTimeKind.Utc);
            var postingReference = $"paycorr:1:{correctionRunId}:{originalPostingId}:reversal";
            var request = FrozenSystemJournalRequest.ForReversal(
                1, postingReference, originalJournalId,
                new ManualJournalReversalInput(
                    $"Payroll correction {correctionReference}", date, date));
            var legacy = new ManualJournalDraftStore(
                db, auditService, new JournalNumberAllocator(db),
                NullLogger<ManualJournalDraftStore>.Instance);
            var frozen = new FrozenSystemJournalStore(db, legacy, audit, auditContext);
            return await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    1, "payroll.accounting.correction_reverse", "Payroll",
                    "Committed the ADR-06 correction reversal operation.", "PayrollRun",
                    correctionRunId.ToString(CultureInfo.InvariantCulture)),
                token => frozen.ReverseAsync(request, 101, token),
                CancellationToken.None);
        }

        public Task<PayrollRunResponse> CreateAsync(string key)
        {
            var request = new CreatePayrollRunRequest("Business", null, key, 2);
            return audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.run.created", "Payroll",
                    "Created a Payroll run and captured eligibility evidence.", "PayrollRun", "1"),
                token => runs.CreateAsync(1, 101, 1, request, token),
                result => !result.IsReplay,
                CancellationToken.None);
        }

        public Task<PayrollPaymentResponse> CreatePaymentForResultAsync(
            long resultId,
            long runVersion,
            IReadOnlyList<PayrollPaymentAllocationInput> allocations)
        {
            var access = CreatePaymentAccess();
            var handler = new PayrollPaymentHandlers(access.Object, payments, audit);
            var request = new CreatePayrollPaymentRequest(
                resultId, runVersion, "cross-artifact-payment", allocations);
            return handler.Handle(new CreatePayrollPaymentCommand(request), CancellationToken.None);
        }

        public Task<PayrollFinalSettlementModel> CalculateFinalSettlementAsync(
            long exitCaseId, long expectedExitVersion, string idempotencyKey) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(
                    1, "payroll.final_settlement.calculated", "Payroll",
                    "Calculated a versioned final settlement from persisted Payroll evidence.",
                    "HrExitCase", exitCaseId.ToString(CultureInfo.InvariantCulture)),
                token => new PayrollFinalSettlementStore(db, TimeProvider.System)
                    .CalculateAsync(1, 101, exitCaseId, expectedExitVersion, idempotencyKey, token),
                result => !result.IsReplay,
                CancellationToken.None);

        public Task<PayrollPaymentBatchResponse> CreateBankBatchAsync(
            long runId, PayrollPaymentResponse payment)
        {
            var access = CreatePaymentAccess();
            var handler = new PayrollPaymentBatchHandlers(access.Object, batches, audit);
            var request = new CreatePayrollPaymentBatchRequest(
                "cross-artifact-batch", "cross-artifact-batch-create",
                [new PayrollPaymentBatchItemInput(payment.Id, payment.Version)]);
            return handler.Handle(
                new CreatePayrollPaymentBatchCommand(runId, request), CancellationToken.None);
        }

        public Task<PayrollPaymentBankFileResult> GenerateBankFileAsync(
            long batchId, long expectedVersion)
        {
            var access = CreatePaymentAccess();
            var handler = new PayrollPaymentBatchHandlers(access.Object, batches, audit);
            return handler.Handle(
                new GeneratePayrollPaymentBankFileCommand(batchId,
                    new PayrollPaymentBankFileRequest(
                        expectedVersion, "cross-artifact-bank-file", "Acceptance bank file")),
                CancellationToken.None);
        }

        public Task<PayrollPaymentBatchResponse> GetBankBatchAsync(long batchId) =>
            batches.GetAsync(1, batchId, CancellationToken.None);

        public PayrollExportService CreatePayrollExportService()
        {
            var access = new Mock<IPayrollRunAccess>();
            access.Setup(value => value.RequireBusinessId()).Returns(1);
            access.Setup(value => value.RequireRealUserId()).Returns(101);
            access.Setup(value => value.EnsurePermissionAsync(
                1, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            access.Setup(value => value.EnsureScopeAsync(
                1, It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var paymentAccess = new Mock<IPayrollEmployeePaymentDestinationAccess>();
            paymentAccess.Setup(value => value.RequireBusinessId()).Returns(1);
            paymentAccess.Setup(value => value.RequireRealUserId()).Returns(101);
            paymentAccess.Setup(value => value.EnsureBusinessPermissionAsync(
                1, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            paymentAccess.Setup(value => value.EnsureAsync(
                1, It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            paymentAccess.Setup(value => value.HasBusinessPermission(It.IsAny<string>()))
                .Returns(true);
            return new PayrollExportService(
                access.Object, paymentAccess.Object, new PayrollExportStore(db),
                new PayrollExportRenderer(), audit,
                Mock.Of<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(settings =>
                    settings.GetAsync(It.IsAny<CancellationToken>()) ==
                    Task.FromResult(new RetailPOS.Application.Settings.SystemSettings())));
        }

        private static Mock<IPayrollPaymentAccess> CreatePaymentAccess()
        {
            var access = new Mock<IPayrollPaymentAccess>();
            access.Setup(value => value.RequireBusinessId()).Returns(1);
            access.Setup(value => value.RequireRealUserId()).Returns(101);
            access.Setup(value => value.EnsureBusinessPermissionAsync(
                    1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(value => value.EnsureLocationAccessibleAsync(
                    1, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            access.Setup(value => value.HasBusinessPermission(It.IsAny<string>())).Returns(true);
            return access;
        }

        public Task<PayrollRunResponse> GetRunAsync(long runId) =>
            runs.GetAsync(1, 1, runId, CancellationToken.None);

        public Task<PayrollRunResponse> CreateCorrectionAsync(
            long originalRunId, string reference, string key, long expectedPeriodVersion,
            long expectedOriginalRunVersion = 4) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.run.correction_created", "Payroll",
                    "Created a linked Payroll correction run.", "PayrollRun", reference),
                token => runs.CreateCorrectionAsync(1, 101, 1, originalRunId,
                    new CreatePayrollCorrectionRequest(reference, "Recalculation",
                        "Concurrent correction test", [1], key, expectedPeriodVersion,
                        expectedOriginalRunVersion), token),
                result => !result.IsReplay,
                CancellationToken.None);

        public async Task<ApprovalDecisionResult> SubmitAndApproveRunAsync(
            long runId, string decisionKey)
        {
            var run = await runs.GetAsync(1, 1, runId, CancellationToken.None);
            var target = await runs.ReadAsync(
                1, PayrollRunApprovalConstants.TargetType,
                runId.ToString(CultureInfo.InvariantCulture), CancellationToken.None)
                ?? throw new InvalidOperationException("Payroll run approval target is missing.");
            var identity = new SessionApprovalIdentity(101);
            var approvals = new ApprovalKernelHandlers(
                new ApprovalKernelStore(db),
                identity,
                new SessionApprovalAccess(),
                new SalarySetupApprovalCandidateResolver(db),
                runs,
                runs,
                audit);
            var submission = await approvals.Handle(new SubmitApprovalCommand(
                ApprovalProcessType.PayrollRun,
                PayrollRunApprovalConstants.TargetType,
                runId.ToString(CultureInfo.InvariantCulture),
                run.ScopeType == PayrollRunScopeTypes.Location
                    ? ApprovalScopeType.Location : ApprovalScopeType.Business,
                run.ScopeId?.ToString(CultureInfo.InvariantCulture),
                target.Version,
                target.Hash), CancellationToken.None);

            var periodVersion = await db.PayrollPeriods.AsNoTracking()
                .Where(row => row.BusinessId == 1 && row.Id == run.PayrollPeriodId)
                .Select(row => row.Version).SingleAsync();
            await LockPeriodAsync(run.PayrollPeriodId, periodVersion);

            identity.RealUserId = 202;
            return await approvals.Handle(new RecordApprovalDecisionCommand(
                submission.InstanceId,
                decisionKey,
                ApprovalDecisionType.Approve,
                "Reviewed frozen Payroll evidence."), CancellationToken.None);
        }

        public async Task<PayrollPeriodCloseResponse> ClosePeriodAsync(
            string key,
            TaskCompletionSource previewed,
            Task waitForRelease)
        {
            var request = new ClosePayrollPeriodRequest(
                key, "PostgreSQL close race acceptance.", 2, 1, []);
            var preview = await periods.PreviewCloseAsync(
                1, 101, 1, request, CancellationToken.None);
            Assert.True(preview.Response.Closed,
                string.Join("; ", preview.Response.Gates
                    .Where(gate => !gate.Passed).Select(gate => gate.Code)));
            previewed.SetResult();
            await waitForRelease;
            var operation = new RequiredAuditOperation(
                1, "payroll.period.closed", "Payroll",
                "Closed a Payroll period.", "PayrollPeriod", "1");
            return await audit.ExecuteAsync(
                operation,
                token => periods.CloseAsync(
                    1, 101, 1, request, preview.StateFingerprint, token),
                result => !result.IsReplay,
                CancellationToken.None);
        }

        public Task<PayrollPeriodResponse> ReopenPeriodAsync(long expectedVersion) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.period.reopened", "Payroll",
                    "Reopened a Payroll period.", "PayrollPeriod", "1"),
                token => periods.ReopenAsync(1, 101, 1,
                    new ReopenPayrollPeriodRequest(expectedVersion,
                        "PostgreSQL close race reopen."), token),
                CancellationToken.None);

        public Task<PayrollPayment> ReadPaymentAsync(long paymentId) =>
            db.PayrollPayments.AsNoTracking().SingleAsync(
                row => row.BusinessId == 1 && row.Id == paymentId);

        public Task<PayrollRunCalculationResponse> CalculateAsync() =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.run.calculated", "Payroll",
                    "Calculated salary components for a Payroll run.", "PayrollRun", "1"),
                token => calculations.CalculateAsync(1, 1, 1, token),
                result => !result.IsReplay,
                CancellationToken.None);

        public Task<PayrollRunCalculationResponse> CalculateAsync(long periodId, long runId) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.run.calculated", "Payroll",
                    "Calculated salary components for a Payroll correction run.", "PayrollRun",
                    runId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                token => calculations.CalculateAsync(1, periodId, runId, token),
                result => !result.IsReplay,
                CancellationToken.None);

        public Task LockPeriodAsync(long periodId, long expectedVersion) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.period.locked", "Payroll",
                    "Locked a Payroll period.", "PayrollPeriod",
                    periodId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                token => periods.LockAsync(1, 101, periodId, expectedVersion, token),
                CancellationToken.None);

        public Task<ApprovalTargetSnapshot?> ReadApprovalTargetAsync(long runId) =>
            runs.ReadAsync(1, PayrollRunApprovalConstants.TargetType,
                runId.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);

        public Task DecidePayrollRunAsync(ApprovalInstance instance, long actorUserId) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "hr.approval.decided", "HR",
                    "Recorded a Payroll Run approval decision.", "ApprovalInstance",
                    instance.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                async token =>
                {
                    await runs.DecisionCompletedAsync(1, instance, actorUserId, "Concurrent approval", token);
                    return true;
                },
                _ => true,
                CancellationToken.None);

        public Task<PayrollPaymentResponse> CreatePaymentAsync(
            string idempotencyKey,
            string? payloadHash = null)
        {
            var request = new CreatePayrollPaymentRequest(
                1, 5, idempotencyKey,
                [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, 1000m)]);
            return audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.payment.created", "Payroll",
                    "Recorded a pending Payroll payment attempt.", "PayrollEmployeeResult", "1"),
                token => payments.CreateAsync(1, 101, request,
                    payloadHash ?? new string('A', 64), token),
                result => !result.IsReplay,
                CancellationToken.None);
        }

        public Task<PayrollPaymentResponse> RecordPaymentEvidenceAsync(
            long paymentId,
            PayrollPaymentEvidenceRequest request,
            string evidenceType,
            string payloadHash)
        {
            var action = evidenceType == PayrollPaymentEvidenceTypes.Return
                ? "payroll.payment.returned"
                : "payroll.payment.settled";
            return audit.ExecuteAsync(
                new RequiredAuditOperation(1, action, "Payroll",
                    evidenceType == PayrollPaymentEvidenceTypes.Return
                        ? "Recorded operator-verified Payroll payment return evidence."
                        : "Recorded operator-verified Payroll payment settlement evidence.",
                    "PayrollPayment", paymentId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                token => payments.RecordEvidenceAsync(
                    1, 101, paymentId, request, payloadHash, evidenceType, token),
                result => !result.IsReplay,
                CancellationToken.None);
        }

        public Task<PayrollPaymentResponse> RetryPaymentAsync(
            long paymentId,
            RetryPayrollPaymentRequest request,
            string payloadHash) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.payment.retried", "Payroll",
                    "Created a linked Payroll payment reissue after resolution evidence.",
                    "PayrollPayment", paymentId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                token => payments.RetryAsync(
                    1, 101, paymentId, request, payloadHash, token),
                result => !result.IsReplay,
                CancellationToken.None);

        public Task<PayrollPaymentBatchResponse> CreateBatchAsync(long paymentId) =>
            audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.payment_batch.created", "Payroll",
                    "Created a ready batch for PostgreSQL concurrency coverage.",
                    "PayrollRun", "1"),
                token => batches.CreateAsync(1, 101, 1,
                    new CreatePayrollPaymentBatchRequest(
                        "concurrent-payment-batch", "concurrent-payment-batch-key",
                        [new PayrollPaymentBatchItemInput(paymentId, 1)]),
                    new string('C', 64), token),
                result => !result.IsReplay,
                CancellationToken.None);

        public Task<PayrollPaymentBatchResponse> SubmitBatchAsync(
            long batchId, long expectedVersion, string idempotencyKey,
            string? externalReference = null)
        {
            var reference = externalReference ?? idempotencyKey;
            var reason = $"Concurrent submission {idempotencyKey}";
            var request = new SubmitPayrollPaymentBatchRequest(
                expectedVersion, idempotencyKey, reference, reason);
            var payloadHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{batchId}:{expectedVersion}:{idempotencyKey}:{reference}:{reason}")));
            return audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.payment_batch.submitted", "Payroll",
                    "Recorded a payment batch submission for PostgreSQL concurrency coverage.",
                    "PayrollPaymentBatch", batchId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                token => batches.SubmitAsync(
                    1, 101, batchId, request, payloadHash, token),
                CancellationToken.None);
        }

        public Task<PayrollPaymentReconciliationOperationResponse> ReconcilePaymentAsync(
            long paymentId,
            long expectedPaymentVersion,
            long expectedReconciliationVersion,
            string idempotencyKey,
            string payloadHash,
            string externalReference,
            bool isException = false)
        {
            var item = new PayrollPaymentReconciliationTargetInput(
                paymentId, expectedPaymentVersion, 1, expectedReconciliationVersion,
                1000m, new DateOnly(2026, 10, 11), PayrollPaymentMethods.Cash,
                externalReference, isException ? "Close race exception" : null, isException);
            return audit.ExecuteAsync(
                new RequiredAuditOperation(1, "payroll.payment.reconciled", "Payroll",
                    "Recorded payment reconciliation for PostgreSQL concurrency coverage.",
                    "PayrollPayment", paymentId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                token => reconciliations.ReconcileAsync(
                    1, 101, null, null, idempotencyKey, payloadHash, [item], token),
                result => !result.IsReplay,
                CancellationToken.None);
        }

        public Task<PayrollPaymentReconciliationOperationResponse> ReconcilePaymentAsync(
            long paymentId,
            long expectedPaymentVersion,
            string idempotencyKey,
            int allocationNo,
            decimal amount,
            string method)
        {
            var access = CreatePaymentAccess();
            var handler = new PayrollPaymentReconciliationHandlers(
                access.Object, reconciliations, audit);
            var request = new ReconcilePayrollPaymentRequest(
                expectedPaymentVersion, 0, idempotencyKey, allocationNo, amount,
                new DateOnly(2026, 10, 15), method,
                "cross-artifact-reconciliation-reference");
            return handler.Handle(
                new ReconcilePayrollPaymentCommand(paymentId, request), CancellationToken.None);
        }

        private sealed class SessionApprovalIdentity(long initialUserId) : IApprovalKernelIdentity
        {
            public long RealUserId { get; set; } = initialUserId;
            public long RequireBusinessId() => 1;
            public long RequireRealUserId() => RealUserId;
            public long? ActingRoleId => null;
            public Guid RequireCorrelationId() => Guid.NewGuid();
        }

        private sealed class SessionApprovalAccess : IApprovalKernelAccess
        {
            public Task EnsureAsync(
                long businessId,
                ApprovalProcessType processType,
                ApprovalKernelOperation operation,
                ApprovalScopeType scopeType,
                string? scopeReference,
                CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                LastException = exception;
        }
    }
}
