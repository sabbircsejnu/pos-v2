using Microsoft.Extensions.Logging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReceiptActionAuditWriter(
    IAuditService auditService,
    ILogger<SaleReceiptActionAuditWriter> logger)
    : ISaleReceiptActionAuditWriter
{
    public async Task RecordBestEffortAsync(
        SaleReceiptActionAuditRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = record.ActionType,
                Module = AuditModule.Sales,
                Summary = record.Summary,
                PrimaryEntity = ("Sale", record.SaleId.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["saleId"] = record.SaleId,
                    ["saleNumber"] = record.SaleNumber,
                    ["outletId"] = record.OutletId,
                    ["terminalId"] = record.TerminalId,
                    ["outputMode"] = record.OutputMode,
                    ["action"] = record.Action,
                    ["message"] = null
                }
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record receipt audit action {Action} for sale {SaleId}",
                record.Action,
                record.SaleId);
        }
    }
}
