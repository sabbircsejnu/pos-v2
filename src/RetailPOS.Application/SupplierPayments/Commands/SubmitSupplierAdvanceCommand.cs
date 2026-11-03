using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record SubmitSupplierAdvanceCommand(
    long AdvanceId,
    long ActorUserId) : ICommand<SupplierAdvanceReadModel>;

public sealed class SubmitSupplierAdvanceCommandHandler(
    ISupplierAdvanceSubmissionStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer,
    ISupplierAdvanceSubmissionAuditWriter auditWriter)
    : IRequestHandler<SubmitSupplierAdvanceCommand, SupplierAdvanceReadModel>
{
    public async Task<SupplierAdvanceReadModel> Handle(
        SubmitSupplierAdvanceCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        var advance = await store.FindForSubmissionAsync(
            request.AdvanceId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Supplier advance {request.AdvanceId} was not found.");

        await authorizer.EnforceOutletAsync(advance.OutletId, cancellationToken);
        if (!string.Equals(advance.Status, "draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only draft supplier advances can be submitted.");

        await store.SubmitAsync(
            request.AdvanceId,
            businessId,
            request.ActorUserId,
            cancellationToken);
        await auditWriter.RecordSubmittedAsync(
            new SupplierAdvanceSubmissionAuditRecord(
                advance.Id,
                businessId,
                advance.OutletId,
                advance.SupplierId,
                advance.Amount,
                advance.RemainingAmount,
                advance.AppliedAmount,
                advance.PaymentMethod,
                advance.PaymentAccountId,
                advance.JournalEntryId,
                advance.SourcePaymentId,
                request.ActorUserId),
            cancellationToken);
        return await store.FindByIdAsync(request.AdvanceId, cancellationToken);
    }
}
