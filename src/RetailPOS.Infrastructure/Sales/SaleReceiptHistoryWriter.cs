using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReceiptHistoryWriter(RetailPOSDbContext dbContext)
    : ISaleReceiptHistoryWriter
{
    public async Task RecordAsync(
        long saleId,
        long? terminalId,
        long? printedByUserId,
        string actionType,
        DateTime printedAtUtc,
        CancellationToken cancellationToken)
    {
        dbContext.ReceiptPrintHistories.Add(new ReceiptPrintHistory
        {
            SaleId = saleId,
            TerminalId = terminalId,
            PrintedByUserId = printedByUserId,
            ActionType = actionType,
            PrintedAt = printedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
