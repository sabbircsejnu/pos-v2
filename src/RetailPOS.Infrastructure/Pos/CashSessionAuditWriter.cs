using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionAuditWriter(
    IAuditService auditService,
    ILogger<CashSessionAuditWriter> logger) : ICashSessionAuditWriter
{
    public async Task RecordAsync(
        string action,
        string summary,
        long cashSessionId,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var values = new Dictionary<string, object?>(metadata)
            {
                ["action"] = action,
                ["cashSessionId"] = cashSessionId
            };
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Sales,
                Summary = summary,
                PrimaryEntity = ("CashSession", cashSessionId.ToString()),
                Status = AuditStatus.Success,
                Metadata = values
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to record cash session audit action {Action} for session {SessionId}", action, cashSessionId);
        }
    }
}
