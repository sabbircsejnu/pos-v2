using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record ApproveSupplierPaymentCommand(long PaymentId, long ActorUserId)
    : ICommand<SupplierPaymentReadModel>;

public sealed class ApproveSupplierPaymentCommandHandler(
    ISupplierPaymentApprovalStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer)
    : IRequestHandler<ApproveSupplierPaymentCommand, SupplierPaymentReadModel>
{
    public async Task<SupplierPaymentReadModel> Handle(
        ApproveSupplierPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        var payment = await store.FindForApprovalAsync(
            request.PaymentId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier payment {request.PaymentId} was not found.");
        await authorizer.EnforceOutletAsync(payment.OutletId, cancellationToken);
        if (!string.Equals(payment.Status, "submitted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only submitted supplier payments can be approved.");

        return await store.ApproveAsync(
            request.PaymentId,
            businessId,
            request.ActorUserId,
            cancellationToken);
    }
}
