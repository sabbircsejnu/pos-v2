using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Commands;

public sealed record PrintSaleReceiptCommand(long SaleId, long? PrintedByUserId)
    : ICommand<SaleReadModel>;

public sealed class PrintSaleReceiptCommandHandler(
    ISaleReadStore saleReadStore,
    ISaleReceiptHistoryWriter historyWriter)
    : IRequestHandler<PrintSaleReceiptCommand, SaleReadModel>
{
    public async Task<SaleReadModel> Handle(
        PrintSaleReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var sale = await saleReadStore.FindByIdAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

        await historyWriter.RecordAsync(
            sale.Id,
            sale.TerminalId,
            request.PrintedByUserId,
            "print",
            DateTime.UtcNow,
            cancellationToken);

        return sale;
    }
}
