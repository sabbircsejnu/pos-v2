using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record ReverseSupplierAdvanceUtilizationCommand(
    long UtilizationId,
    DateTime PostingDate,
    string? Reason,
    long ActorUserId) : ICommand;

public sealed class ReverseSupplierAdvanceUtilizationCommandHandler(
    ISupplierAdvanceUtilizationStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer)
    : IRequestHandler<ReverseSupplierAdvanceUtilizationCommand>
{
    public async Task Handle(
        ReverseSupplierAdvanceUtilizationCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Reversal reason is required.");

        var utilization = await store.FindForReversalAsync(
            request.UtilizationId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Effective supplier advance utilization {request.UtilizationId} was not found.");
        await authorizer.EnforceOutletAsync(utilization.OutletId, cancellationToken);

        await store.ReverseAsync(
            request.UtilizationId,
            businessId,
            request.PostingDate.Date,
            request.Reason.Trim(),
            request.ActorUserId,
            cancellationToken);
    }
}
