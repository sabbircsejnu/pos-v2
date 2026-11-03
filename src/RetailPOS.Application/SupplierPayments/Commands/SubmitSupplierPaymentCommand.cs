using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record SubmitSupplierPaymentCommand(long PaymentId, long ActorUserId)
    : ICommand<SupplierPaymentReadModel>;

public sealed class SubmitSupplierPaymentCommandHandler(
    ISupplierPaymentSubmissionStore store,
    ISupplierPaymentWorkflowAuthorizer authorizer,
    ISupplierPaymentSubmissionAuditWriter auditWriter)
    : IRequestHandler<SubmitSupplierPaymentCommand, SupplierPaymentReadModel>
{
    public async Task<SupplierPaymentReadModel> Handle(
        SubmitSupplierPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await authorizer.GetBusinessIdAsync(cancellationToken);
        var payment = await store.FindForSubmissionAsync(
            request.PaymentId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier payment {request.PaymentId} was not found.");
        await authorizer.EnforceOutletAsync(payment.OutletId, cancellationToken);
        if (!string.Equals(payment.Status, "draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only draft supplier payments can be submitted.");

        await store.SubmitAsync(
            payment.Id,
            businessId,
            request.ActorUserId,
            cancellationToken);
        await auditWriter.RecordSubmittedAsync(
            new SupplierPaymentSubmissionAuditRecord(
                payment.Id,
                businessId,
                payment.OutletId,
                payment.SupplierId,
                payment.Amount,
                payment.PaymentMethod,
                payment.PaymentAccountId,
                payment.JournalEntryId,
                payment.ReversalJournalEntryId,
                request.ActorUserId),
            cancellationToken);
        return await store.FindByIdAsync(payment.Id, cancellationToken);
    }
}
