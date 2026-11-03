using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionCloseAuditWriter(
    IAuditService auditService,
    ILogger<CashSessionCloseAuditWriter> logger) : ICashSessionCloseAuditWriter
{
    public async Task RecordClosedAsync(
        CashSessionSnapshot session,
        decimal expectedClosingCash,
        decimal actualClosingCash,
        decimal difference,
        long? varianceApprovalId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Sales,
                Summary = $"Cash session closed for outlet {session.OutletId} terminal {session.TerminalId}",
                PrimaryEntity = ("CashSession", session.Id.ToString()),
                Status = AuditStatus.Success,
                Metadata = new Dictionary<string, object?>
                {
                    ["action"] = "CashSessionClosed",
                    ["cashSessionId"] = session.Id,
                    ["outletId"] = session.OutletId,
                    ["terminalId"] = session.TerminalId,
                    ["businessDate"] = session.BusinessDate,
                    ["openedAt"] = session.OpenedAt,
                    ["closedAt"] = session.ClosedAt,
                    ["expectedClosingCashAmount"] = expectedClosingCash,
                    ["actualClosingCashAmount"] = actualClosingCash,
                    ["differenceAmount"] = difference,
                    ["varianceApprovalId"] = varianceApprovalId,
                    ["closingNotes"] = session.ClosingNotes
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record cash session audit action CashSessionClosed for session {SessionId}",
                session.Id);
        }
    }
}
