using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record CreateSupplierAdvanceCommand(
    long SupplierId,
    long? OutletId,
    DateTime PaymentDate,
    string PaymentMethod,
    long PaymentAccountId,
    decimal Amount,
    string? ReferenceNumber,
    string? Notes,
    long ActorUserId) : ICommand<SupplierAdvanceReadModel>;

public sealed class CreateSupplierAdvanceCommandHandler(
    ISupplierAdvanceCreationStore store,
    ISupplierPaymentCreationAuthorizer authorizer,
    ISupplierAdvanceCreationAuditWriter auditWriter)
    : IRequestHandler<CreateSupplierAdvanceCommand, SupplierAdvanceReadModel>
{
    public async Task<SupplierAdvanceReadModel> Handle(
        CreateSupplierAdvanceCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await authorizer.AuthorizeAsync(request.OutletId, cancellationToken);
        if (!await store.SupplierExistsAsync(scope.BusinessId, request.SupplierId, cancellationToken))
            throw new InvalidOperationException($"Supplier {request.SupplierId} was not found.");
        if (!await store.PaymentAccountExistsAsync(
                scope.BusinessId,
                request.PaymentAccountId,
                cancellationToken))
            throw new InvalidOperationException("Payment account was not found.");

        var paymentDate = request.PaymentDate.Date;
        var paymentMethod = NormalizeRequired(request.PaymentMethod, "Payment method", 40);
        var advance = new SupplierAdvance
        {
            BusinessId = scope.BusinessId,
            OutletId = scope.OutletId,
            SupplierId = request.SupplierId,
            PaymentDate = paymentDate,
            BusinessDate = paymentDate,
            PostingDate = paymentDate,
            PaymentMethod = paymentMethod,
            PaymentAccountId = request.PaymentAccountId,
            Amount = request.Amount,
            RemainingAmount = request.Amount,
            AppliedAmount = 0,
            Status = "draft",
            ReferenceNumber = NormalizeOptional(request.ReferenceNumber, 80),
            Notes = NormalizeOptional(request.Notes, 500),
            IdempotencyKey = BuildIdempotencyKey(
                scope.BusinessId,
                request.SupplierId,
                request.PaymentDate,
                request.ReferenceNumber),
            CreatedBy = request.ActorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = request.ActorUserId,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await store.CreateAsync(advance, cancellationToken);
        await auditWriter.RecordCreatedAsync(
            new SupplierAdvanceCreationAuditRecord(
                advance.Id,
                advance.BusinessId,
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
        return created;
    }

    private static string NormalizeRequired(string? value, string fieldName, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"{fieldName} is required.");

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string BuildIdempotencyKey(
        long businessId,
        long supplierId,
        DateTime paymentDate,
        string? referenceNumber)
    {
        var reference = string.IsNullOrWhiteSpace(referenceNumber)
            ? "default"
            : referenceNumber.Trim();
        return $"{businessId}:{supplierId}:{paymentDate:yyyyMMdd}:{reference}";
    }
}
