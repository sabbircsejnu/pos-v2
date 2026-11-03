using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionOpenAuditWriter(
    IAuditService auditService,
    ILogger<CashSessionOpenAuditWriter> logger) : ICashSessionOpenAuditWriter
{
    public async Task RecordOpenedAsync(
        CashSessionSnapshot session,
        decimal openingCashAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.Sales,
                Summary = $"Cash session opened for outlet {session.OutletId} terminal {session.TerminalId}",
                PrimaryEntity = ("CashSession", session.Id.ToString()),
                Status = AuditStatus.Success,
                Metadata = new Dictionary<string, object?>
                {
                    ["action"] = "CashSessionOpened",
                    ["cashSessionId"] = session.Id,
                    ["outletId"] = session.OutletId,
                    ["terminalId"] = session.TerminalId,
                    ["businessDate"] = session.BusinessDate,
                    ["openedAt"] = session.OpenedAt,
                    ["closedAt"] = session.ClosedAt,
                    ["openingCashAmount"] = openingCashAmount,
                    ["openingNotes"] = session.OpeningNotes
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
                "Failed to record cash session audit action CashSessionOpened for session {SessionId}",
                session.Id);
        }
    }
}
