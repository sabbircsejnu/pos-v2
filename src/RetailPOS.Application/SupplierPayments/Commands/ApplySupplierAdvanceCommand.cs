using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record ApplySupplierAdvanceCommand(
    long AdvanceId,
    long SupplierPayableDocumentId,
    decimal Amount,
    DateTime EffectiveDate,
    string? ReferenceNumber,
    string? Notes,
    long ActorUserId) : ICommand;

public sealed class ApplySupplierAdvanceCommandHandler(
    ISupplierAdvanceUtilizationStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer)
    : IRequestHandler<ApplySupplierAdvanceCommand>
{
    public async Task Handle(
        ApplySupplierAdvanceCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.ReferenceNumber))
            throw new InvalidOperationException(
                "A positive amount and reference number are required to apply a supplier advance.");

        var advance = await store.FindPostedAdvanceAsync(
            request.AdvanceId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Posted supplier advance {request.AdvanceId} was not found.");
        await authorizer.EnforceOutletAsync(advance.OutletId, cancellationToken);

        var payable = await store.FindPayableAsync(
            request.SupplierPayableDocumentId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Supplier payable document {request.SupplierPayableDocumentId} was not found.");
        if (advance.SupplierId != payable.SupplierId)
            throw new InvalidOperationException(
                "Supplier advance and payable document must belong to the same supplier.");

        await store.ApplyAsync(
            advance.Id,
            payable.Id,
            businessId,
            request.Amount,
            request.EffectiveDate,
            request.ReferenceNumber.Trim(),
            request.Notes,
            request.ActorUserId,
            cancellationToken);
    }
}
