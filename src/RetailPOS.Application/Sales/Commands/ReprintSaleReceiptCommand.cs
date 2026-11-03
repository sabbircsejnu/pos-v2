using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Commands;

public sealed record ReprintSaleReceiptCommand(long SaleId, long? PrintedByUserId)
    : ICommand<SaleReadModel>;

public sealed class ReprintSaleReceiptCommandHandler(
    ISaleReadStore saleReadStore,
    ISaleReceiptHistoryWriter historyWriter)
    : IRequestHandler<ReprintSaleReceiptCommand, SaleReadModel>
{
    public async Task<SaleReadModel> Handle(
        ReprintSaleReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var sale = await saleReadStore.FindByIdAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

        if (!string.Equals(sale.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sale.Status, "voided", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sale.Status, "refunded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only finalized sales can be reprinted");
        }

        await historyWriter.RecordAsync(
            sale.Id,
            sale.TerminalId,
            request.PrintedByUserId,
            "reprint",
            DateTime.UtcNow,
            cancellationToken);

        return sale;
    }
}
