using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

/// <summary>Upserts and reads worker heartbeats. Runs outside any posting transaction.</summary>
public sealed class FinancialPostingHeartbeatStore(RetailPOSDbContext dbContext)
{
    public async Task UpsertAsync(FinancialPostingWorkerHeartbeat beat, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            var existing = await dbContext.FinancialPostingWorkerHeartbeats.FindAsync(
                [beat.WorkerName, beat.InstanceId], cancellationToken);
            if (existing is null)
                dbContext.FinancialPostingWorkerHeartbeats.Add(beat);
            else
                dbContext.Entry(existing).CurrentValues.SetValues(beat);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_posting_worker_heartbeats
                (worker_name, instance_id, host_name, host_role, started_at_utc, last_run_started_at_utc,
                 last_run_completed_at_utc, last_batch_due, last_batch_failures, consecutive_run_failures, last_error, updated_at_utc)
            VALUES ({beat.WorkerName}, {beat.InstanceId}, {beat.HostName}, {beat.HostRole}, {beat.StartedAtUtc},
                    {beat.LastRunStartedAtUtc}, {beat.LastRunCompletedAtUtc}, {beat.LastBatchDue}, {beat.LastBatchFailures},
                    {beat.ConsecutiveRunFailures}, {beat.LastError}, {beat.UpdatedAtUtc})
            ON CONFLICT (worker_name, instance_id) DO UPDATE SET
                host_name = EXCLUDED.host_name, host_role = EXCLUDED.host_role,
                last_run_started_at_utc = EXCLUDED.last_run_started_at_utc,
                last_run_completed_at_utc = EXCLUDED.last_run_completed_at_utc,
                last_batch_due = EXCLUDED.last_batch_due, last_batch_failures = EXCLUDED.last_batch_failures,
                consecutive_run_failures = EXCLUDED.consecutive_run_failures, last_error = EXCLUDED.last_error,
                updated_at_utc = EXCLUDED.updated_at_utc
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<FinancialPostingWorkerHeartbeat>> ListAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
        await dbContext.FinancialPostingWorkerHeartbeats.AsNoTracking()
            .Where(beat => beat.UpdatedAtUtc >= sinceUtc)
            .OrderBy(beat => beat.WorkerName).ThenBy(beat => beat.InstanceId)
            .ToListAsync(cancellationToken);
}
