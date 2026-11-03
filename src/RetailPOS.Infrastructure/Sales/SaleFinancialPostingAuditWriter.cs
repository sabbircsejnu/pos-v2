using Microsoft.Extensions.Logging;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleFinancialPostingAuditWriter(
    IAuditService auditService,
    ILogger<SaleFinancialPostingAuditWriter> logger) : ISaleFinancialPostingAuditWriter
{
    public async Task RecordRetryAsync(SaleFinancialPostingRetryAudit retry, CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Sales,
            Summary = $"Financial posting {retry.PostingId} retry requested ({retry.PreviousStatus} -> {retry.NewStatus})",
            PrimaryEntity = (nameof(SaleFinancialPosting), retry.PostingId.ToString()),
            Status = AuditStatus.Success,
            Metadata = new Dictionary<string, object?>
            {
                ["action"] = "SaleFinancialPostingRetry",
                ["businessId"] = retry.BusinessId,
                ["postingId"] = retry.PostingId,
                ["sourceKey"] = retry.SourceKey,
                ["previousStatus"] = retry.PreviousStatus,
                ["retryRequestedAtUtc"] = retry.RequestedAtUtc,
                ["outcome"] = retry.Outcome,
                ["newStatus"] = retry.NewStatus
            }
        }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The retry itself has committed; a lost audit row is logged, never reported as a failed retry.
            logger.LogWarning(exception, "Failed to audit retry of financial posting {PostingId}", retry.PostingId);
        }
    }
}
