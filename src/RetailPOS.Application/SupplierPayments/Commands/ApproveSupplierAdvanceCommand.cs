using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record ApproveSupplierAdvanceCommand(long AdvanceId, long ActorUserId)
    : ICommand<SupplierAdvanceReadModel>;

public sealed class ApproveSupplierAdvanceCommandHandler(
    ISupplierAdvanceApprovalStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer)
    : IRequestHandler<ApproveSupplierAdvanceCommand, SupplierAdvanceReadModel>
{
    public async Task<SupplierAdvanceReadModel> Handle(
        ApproveSupplierAdvanceCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        var advance = await store.FindForApprovalAsync(
            request.AdvanceId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Supplier advance {request.AdvanceId} was not found.");

        await authorizer.EnforceOutletAsync(advance.OutletId, cancellationToken);
        if (!string.Equals(advance.Status, "submitted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only submitted supplier advances can be approved.");

        return await store.ApproveAsync(
            request.AdvanceId,
            businessId,
            request.ActorUserId,
            cancellationToken);
    }
}
