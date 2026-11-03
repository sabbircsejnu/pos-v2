using System.Collections.Concurrent;
using MediatR;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Sales;

namespace RetailPOS.API.Services;

public static class FinancialPostingHostRoles
{
    /// <summary>Same process: the API also runs the enabled financial workers (Development, single instance).</summary>
    public const string Api = "Api";

    /// <summary>Dedicated worker process: only the financial workers and a health endpoint; no API endpoints.</summary>
    public const string Worker = "Worker";
}

/// <summary>
/// Which financial workers this process runs (P3.2-D05). The same build runs as an API (<c>FinancialPosting:HostRole =
/// Api</c>, workers per <c>FinancialPosting:*:WorkerEnabled</c>) or as a dedicated worker (<c>Worker</c>). Several
/// processes may run the same worker: the row lock with SKIP LOCKED keeps every posting single-claimed.
/// </summary>
public sealed record FinancialPostingWorkerPlan(string Role, bool RunSales, bool RunCashDifferences)
{
    public bool WorkerOnly => Role == FinancialPostingHostRoles.Worker;

    public static FinancialPostingWorkerPlan Resolve(string? role, bool salesEnabled, bool cashDifferencesEnabled, bool isTesting)
    {
        var normalized = string.IsNullOrWhiteSpace(role) ? FinancialPostingHostRoles.Api : role.Trim();
        if (string.Equals(normalized, FinancialPostingHostRoles.Worker, StringComparison.OrdinalIgnoreCase))
        {
            if (!salesEnabled && !cashDifferencesEnabled)
                throw new InvalidOperationException(
                    "FinancialPosting:HostRole=Worker needs at least one FinancialPosting:*:WorkerEnabled=true.");
            return new FinancialPostingWorkerPlan(FinancialPostingHostRoles.Worker, salesEnabled, cashDifferencesEnabled);
        }

        if (!string.Equals(normalized, FinancialPostingHostRoles.Api, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FinancialPosting:HostRole must be 'Api' or 'Worker'.");
        // The automated-test host never starts background workers implicitly; tests drive the runners directly.
        return new FinancialPostingWorkerPlan(
            FinancialPostingHostRoles.Api, salesEnabled && !isTesting, cashDifferencesEnabled && !isTesting);
    }
}

/// <summary>Identity of this process in the durable heartbeat table.</summary>
public sealed class FinancialPostingInstance(string role)
{
    public string Role { get; } = role;
    public string HostName { get; } = Truncate(Environment.MachineName, 120);
    public string InstanceId { get; } = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 120);
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

/// <summary>
/// Copies a worker's in-process heartbeat to <c>financial_posting_worker_heartbeats</c> after every pass, so the API
/// can monitor a dedicated worker process. Best effort: when the database is down the in-process health still shows it.
/// </summary>
public sealed class FinancialPostingHeartbeatPublisher(
    IServiceScopeFactory scopes,
    FinancialPostingWorkerHealth health,
    FinancialPostingInstance instance,
    TimeProvider clock,
    ILogger<FinancialPostingHeartbeatPublisher> logger)
{
    public async Task PublishAsync(string worker, CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot().FirstOrDefault(entry => entry.Worker == worker);
        if (snapshot is null)
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FinancialPostingHeartbeatStore>().UpsertAsync(
                new Core.Entities.FinancialPostingWorkerHeartbeat
                {
                    WorkerName = worker,
                    InstanceId = instance.InstanceId,
                    HostName = instance.HostName,
                    HostRole = instance.Role,
                    StartedAtUtc = instance.StartedAtUtc,
                    LastRunStartedAtUtc = snapshot.LastRunStartedAtUtc,
                    LastRunCompletedAtUtc = snapshot.LastRunCompletedAtUtc,
                    LastBatchDue = snapshot.LastBatchSize,
                    LastBatchFailures = snapshot.LastBatchFailures,
                    ConsecutiveRunFailures = snapshot.ConsecutiveRunFailures,
                    LastError = snapshot.LastError,
                    UpdatedAtUtc = clock.GetUtcNow().UtcDateTime
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not record the {Worker} heartbeat ({FailureType}).", worker, exception.GetType().Name);
        }
    }
}

/// <summary>Variance worker settings (configuration section <c>FinancialPosting:CashDifferences</c>).</summary>
public sealed class CashDifferencePostingWorkerOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 20;

    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 3600)
            throw new InvalidOperationException("FinancialPosting:CashDifferences:PollIntervalSeconds must be between 1 and 3600.");
        if (BatchSize is < 1 or > 500)
            throw new InvalidOperationException("FinancialPosting:CashDifferences:BatchSize must be between 1 and 500.");
    }
}

public sealed record FinancialPostingWorkerHealthSnapshot(
    string Worker,
    DateTime? LastRunStartedAtUtc,
    DateTime? LastRunCompletedAtUtc,
    int LastBatchSize,
    int LastBatchFailures,
    int ConsecutiveRunFailures,
    string? LastError);

/// <summary>Process-local heartbeat of the posting workers, exposed by <c>GET api/financial-postings/summary</c>.</summary>
public sealed class FinancialPostingWorkerHealth
{
    private readonly ConcurrentDictionary<string, FinancialPostingWorkerHealthSnapshot> _workers = new();

    public void RunStarted(string worker, DateTime nowUtc) =>
        _workers.AddOrUpdate(
            worker,
            _ => new FinancialPostingWorkerHealthSnapshot(worker, nowUtc, null, 0, 0, 0, null),
            (_, current) => current with { LastRunStartedAtUtc = nowUtc });

    public void RunCompleted(string worker, DateTime nowUtc, int batchSize, int failures) =>
        _workers.AddOrUpdate(
            worker,
            _ => new FinancialPostingWorkerHealthSnapshot(worker, nowUtc, nowUtc, batchSize, failures, 0, null),
            (_, current) => current with
            {
                LastRunCompletedAtUtc = nowUtc,
                LastBatchSize = batchSize,
                LastBatchFailures = failures,
                ConsecutiveRunFailures = 0
            });

    public void RunFailed(string worker, string error) =>
        _workers.AddOrUpdate(
            worker,
            _ => new FinancialPostingWorkerHealthSnapshot(worker, null, null, 0, 0, 1, error),
            (_, current) => current with
            {
                ConsecutiveRunFailures = current.ConsecutiveRunFailures + 1,
                LastError = error
            });

    public IReadOnlyList<FinancialPostingWorkerHealthSnapshot> Snapshot() =>
        _workers.Values.OrderBy(worker => worker.Worker, StringComparer.Ordinal).ToArray();
}

public sealed record FinancialPostingBatchResult(int Due, int Posted, int Failed, int Skipped);

/// <summary>
/// One bounded pass over due sale postings of every Business. Each posting runs in its own DI scope whose audit
/// context carries the posting's Business explicitly (no HTTP user), so account resolution, journals and outbox are
/// confined to that Business. The row lock with SKIP LOCKED is the claim: another instance skips a row this one holds.
/// </summary>
public sealed class SaleFinancialPostingBatchRunner(
    IServiceScopeFactory scopes,
    SaleFinancialPostingOptions options,
    TimeProvider clock,
    ILogger<SaleFinancialPostingBatchRunner> logger)
{
    public const string WorkerName = "sale-financial-posting-worker";

    public async Task<FinancialPostingBatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SaleFinancialPostingDue> due;
        await using (var listScope = scopes.CreateAsyncScope())
        {
            due = await listScope.ServiceProvider.GetRequiredService<ISaleFinancialPostingStore>()
                .ListDueAsync(clock.GetUtcNow().UtcDateTime, options.BatchSize, cancellationToken);
        }

        int posted = 0, failed = 0, skipped = 0;
        foreach (var item in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                FinancialPostingJobAudit.Initialize(scope.ServiceProvider, WorkerName, item.BusinessId,
                    item.ActorUserId, item.OutletId);
                var result = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ProcessSaleFinancialPostingCommand(item.Id, SkipIfLocked: true), cancellationToken);
                switch (result.Outcome)
                {
                    case SaleFinancialPostingOutcome.Posted:
                        posted++;
                        logger.LogInformation(
                            "Financial posting {PostingId} ({SourceKey}) posted to journal {JournalId} for business {BusinessId}.",
                            item.Id, result.Posting.SourceKey, result.Posting.JournalEntryId, item.BusinessId);
                        break;
                    case SaleFinancialPostingOutcome.Failed:
                        failed++;
                        logger.LogWarning(
                            "Financial posting {PostingId} ({SourceKey}) failed attempt {Attempt} ({ErrorCode}); next attempt {NextAttemptAtUtc:o}.",
                            item.Id, result.Posting.SourceKey, result.Posting.Attempts, result.Posting.LastErrorCode,
                            result.Posting.NextAttemptAtUtc);
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The posting row is untouched (or rolled back) and stays due; the next pass retries it.
                failed++;
                logger.LogError(
                    "Financial posting {PostingId} could not be processed ({FailureType}); it remains due.",
                    item.Id, exception.GetType().Name);
            }
        }

        return new FinancialPostingBatchResult(due.Count, posted, failed, skipped);
    }
}

/// <summary>
/// One bounded pass over approved cash differences due for posting in every Business. An unapproved difference is
/// never listed and is refused again under the row lock by <see cref="PostCashDifferenceCommand"/>.
/// </summary>
public sealed class CashDifferencePostingBatchRunner(
    IServiceScopeFactory scopes,
    CashDifferencePostingWorkerOptions options,
    TimeProvider clock,
    ILogger<CashDifferencePostingBatchRunner> logger)
{
    public const string WorkerName = "cash-difference-posting-worker";

    public async Task<FinancialPostingBatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CashDifferencePostingDue> due;
        await using (var listScope = scopes.CreateAsyncScope())
        {
            due = await listScope.ServiceProvider.GetRequiredService<ICashDifferenceStore>()
                .ListDueForPostingAcrossBusinessesAsync(clock.GetUtcNow().UtcDateTime, options.BatchSize, cancellationToken);
        }

        int posted = 0, failed = 0, skipped = 0;
        foreach (var item in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                FinancialPostingJobAudit.Initialize(scope.ServiceProvider, WorkerName, item.BusinessId,
                    item.ReviewerUserId, item.OutletId);
                var result = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new PostCashDifferenceCommand(item.ApprovalId, item.ReviewerUserId, SkipIfLocked: true),
                        cancellationToken);
                if (result.Replayed)
                    skipped++;
                else if (result.Approval.PostingStatus == Core.Entities.CashDifferencePostingStatuses.Posted)
                {
                    posted++;
                    logger.LogInformation(
                        "Cash difference {ApprovalId} posted to journal {JournalId} for business {BusinessId}.",
                        item.ApprovalId, result.Approval.JournalEntryId, item.BusinessId);
                }
                else
                {
                    failed++;
                    logger.LogWarning(
                        "Cash difference {ApprovalId} posting failed attempt {Attempt}; next attempt {NextAttemptAtUtc:o}.",
                        item.ApprovalId, result.Approval.PostingAttempts, result.Approval.NextPostingAttemptAtUtc);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogError(
                    "Cash difference {ApprovalId} could not be processed ({FailureType}); it remains due.",
                    item.ApprovalId, exception.GetType().Name);
            }
        }

        return new FinancialPostingBatchResult(due.Count, posted, failed, skipped);
    }
}

internal static class FinancialPostingJobAudit
{
    /// <summary>
    /// Integration-sourced audit context: the persisted actor (cashier/reviewer) stays the business actor, the worker
    /// is identified by the request path, and the Business is explicit - the only Business this scope may touch.
    /// </summary>
    public static void Initialize(
        IServiceProvider services,
        string workerName,
        long businessId,
        long actorUserId,
        long outletId) =>
        services.GetRequiredService<IAuditContext>().Initialize(
            AuditContext.ForBusinessSystemJob(workerName, businessId) with
            {
                Source = AuditSource.Integration,
                RealUserId = actorUserId,
                ActingUserId = actorUserId,
                OutletId = outletId
            });
}

public sealed class SaleFinancialPostingHostedService(
    SaleFinancialPostingBatchRunner runner,
    SaleFinancialPostingSignal signal,
    SaleFinancialPostingOptions options,
    FinancialPostingWorkerHealth health,
    FinancialPostingHeartbeatPublisher heartbeat,
    TimeProvider clock,
    ILogger<SaleFinancialPostingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sale financial posting worker started (poll {PollSeconds}s, batch {BatchSize}).",
            options.PollIntervalSeconds, options.BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            var fullBatch = false;
            try
            {
                health.RunStarted(SaleFinancialPostingBatchRunner.WorkerName, clock.GetUtcNow().UtcDateTime);
                var result = await runner.RunOnceAsync(stoppingToken);
                health.RunCompleted(SaleFinancialPostingBatchRunner.WorkerName, clock.GetUtcNow().UtcDateTime,
                    result.Due, result.Failed);
                fullBatch = result.Due >= options.BatchSize;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                health.RunFailed(SaleFinancialPostingBatchRunner.WorkerName, exception.GetType().Name);
                logger.LogError("Sale financial posting pass failed ({FailureType}); postings remain durable.",
                    exception.GetType().Name);
            }

            await PublishHeartbeatAsync(SaleFinancialPostingBatchRunner.WorkerName, stoppingToken);

            if (fullBatch)
                continue;
            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Sale financial posting worker stopped.");
    }

    private async Task PublishHeartbeatAsync(string worker, CancellationToken stoppingToken)
    {
        try
        {
            await heartbeat.PublishAsync(worker, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

public sealed class CashDifferencePostingHostedService(
    CashDifferencePostingBatchRunner runner,
    CashDifferencePostingWorkerOptions options,
    FinancialPostingWorkerHealth health,
    FinancialPostingHeartbeatPublisher heartbeat,
    TimeProvider clock,
    ILogger<CashDifferencePostingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Cash difference posting worker started (poll {PollSeconds}s, batch {BatchSize}).",
            options.PollIntervalSeconds, options.BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                health.RunStarted(CashDifferencePostingBatchRunner.WorkerName, clock.GetUtcNow().UtcDateTime);
                var result = await runner.RunOnceAsync(stoppingToken);
                health.RunCompleted(CashDifferencePostingBatchRunner.WorkerName, clock.GetUtcNow().UtcDateTime,
                    result.Due, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                health.RunFailed(CashDifferencePostingBatchRunner.WorkerName, exception.GetType().Name);
                logger.LogError("Cash difference posting pass failed ({FailureType}); approvals remain durable.",
                    exception.GetType().Name);
            }

            await PublishHeartbeatAsync(CashDifferencePostingBatchRunner.WorkerName, stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Cash difference posting worker stopped.");
    }

    private async Task PublishHeartbeatAsync(string worker, CancellationToken stoppingToken)
    {
        try
        {
            await heartbeat.PublishAsync(worker, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
