namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public sealed record MobileStockTakeSubmissionOptions(
    int MaxAttempts = 5,
    int LeaseSeconds = 300,
    int BaseRetrySeconds = 10,
    int MaxRetrySeconds = 900,
    int AcceptedRecoveryGraceSeconds = 60,
    int SweepBatchSize = 20,
    int SweepIntervalSeconds = 5)
{
    public void Validate()
    {
        if (MaxAttempts is < 1 or > 20
            || LeaseSeconds < 30
            || BaseRetrySeconds < 1
            || MaxRetrySeconds < BaseRetrySeconds
            || AcceptedRecoveryGraceSeconds < 0
            || SweepBatchSize is < 1 or > 200
            || SweepIntervalSeconds < 1)
        {
            throw new InvalidOperationException("Mobile Stock Take submission worker bounds are invalid.");
        }
    }
}

public interface IMobileStockTakeSubmissionProcessor
{
    /// <summary>
    /// Idempotently claims and processes one durable submission job. Safe under message redelivery,
    /// duplicate consumers and worker restarts.
    /// </summary>
    Task<MobileStockTakeSubmissionProcessingOutcome> ProcessAsync(
        Guid jobId,
        long? expectedBusinessId,
        CancellationToken cancellationToken);

    /// <summary>Finds jobs whose retry is due or whose processing lease expired or whose message never arrived.</summary>
    Task<IReadOnlyList<Guid>> FindRecoverableJobIdsAsync(CancellationToken cancellationToken);
}
