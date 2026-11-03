using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;

namespace RetailPOS.Application.Sales.Commands;

public sealed record RegisterSaleReceiptAttemptCommand(
    long SaleId,
    string? Action,
    string? OutputMode,
    string? Message,
    long? ActorUserId) : ICommand;

public sealed class RegisterSaleReceiptAttemptCommandHandler(
    ISaleReadStore saleReadStore,
    ISaleReceiptHistoryWriter historyWriter,
    ISaleReceiptAttemptAuditWriter auditWriter)
    : IRequestHandler<RegisterSaleReceiptAttemptCommand>
{
    public async Task Handle(
        RegisterSaleReceiptAttemptCommand request,
        CancellationToken cancellationToken)
    {
        var sale = await saleReadStore.FindByIdAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");

        var action = NormalizeAction(request.Action);
        var failed = action == "print_failed";
        var actionType = action switch
        {
            "generated" => "ReceiptGenerated",
            "printed" => "ReceiptPrinted",
            "reprinted" => "ReceiptReprinted",
            "saved_pdf" => "ReceiptSavedAsPdf",
            _ => "PrintFailure"
        };

        await historyWriter.RecordAsync(
            sale.Id,
            sale.TerminalId,
            request.ActorUserId,
            ToHistoryAction(action),
            DateTime.UtcNow,
            cancellationToken);

        await auditWriter.RecordBestEffortAsync(
            new SaleReceiptAttemptAuditRecord(
                actionType,
                $"Receipt {action} for {sale.SaleNumber}",
                failed ? "Failed" : "Success",
                failed ? request.Message : null,
                sale.Id,
                sale.SaleNumber,
                sale.OutletId,
                sale.TerminalId,
                request.OutputMode,
                action,
                request.Message),
            cancellationToken);
    }

    private static string NormalizeAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "print_failed";
        }

        return action.Trim().ToLowerInvariant() switch
        {
            "generated" => "generated",
            "printed" => "printed",
            "reprinted" => "reprinted",
            "savedaspdf" or "saved_pdf" => "saved_pdf",
            "printfailed" or "print_failed" => "print_failed",
            _ => "print_failed"
        };
    }

    private static string ToHistoryAction(string action) => action switch
    {
        "generated" => "generated",
        "printed" => "printed",
        "reprinted" => "reprint",
        "saved_pdf" => "saved_pdf",
        _ => "print_failed"
    };
}
