namespace RetailPOS.Core.Entities;

/// <summary>
/// Durable liveness of one financial-posting worker instance (P3.2-D05). Operational data only: it carries no Business
/// data, so a dedicated worker process and every API instance can be monitored from the same table.
/// </summary>
public class FinancialPostingWorkerHeartbeat
{
    public string WorkerName { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;

    /// <summary><c>Api</c> (same-process) or <c>Worker</c> (dedicated worker process).</summary>
    public string HostRole { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }
    public DateTime? LastRunStartedAtUtc { get; set; }
    public DateTime? LastRunCompletedAtUtc { get; set; }
    public int LastBatchDue { get; set; }
    public int LastBatchFailures { get; set; }
    public int ConsecutiveRunFailures { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
