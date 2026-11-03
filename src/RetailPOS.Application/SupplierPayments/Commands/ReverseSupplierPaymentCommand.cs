using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record ReverseSupplierPaymentCommand(
    long PaymentId,
    DateTime PostingDate,
    string? Reason,
    long ActorUserId) : ICommand<SupplierPaymentReadModel>;

public sealed class ReverseSupplierPaymentCommandHandler(
    ISupplierPaymentReversalStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer)
    : IRequestHandler<ReverseSupplierPaymentCommand, SupplierPaymentReadModel>
{
    public async Task<SupplierPaymentReadModel> Handle(
        ReverseSupplierPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Reversal reason is required.");

        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        var payment = await store.FindForReversalAsync(
            request.PaymentId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier payment {request.PaymentId} was not found.");
        await authorizer.EnforceOutletAsync(payment.OutletId, cancellationToken);

        return await store.ReverseAsync(
            request.PaymentId,
            businessId,
            request.PostingDate.Date,
            request.Reason.Trim(),
            request.ActorUserId,
            cancellationToken);
    }
}
