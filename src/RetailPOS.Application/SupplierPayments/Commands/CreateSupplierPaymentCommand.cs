using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Commands;

public sealed record CreateSupplierPaymentAllocationCommand(
    long BillId,
    long? SupplierPayableDocumentId,
    decimal Amount);

public sealed record CreateSupplierPaymentCommand(
    long SupplierId,
    long? OutletId,
    DateTime PaymentDate,
    string PaymentMethod,
    long PaymentAccountId,
    decimal Amount,
    string? ReferenceNumber,
    string? Notes,
    IReadOnlyList<CreateSupplierPaymentAllocationCommand>? Allocations,
    long ActorUserId) : ICommand<SupplierPaymentReadModel>;

public sealed class CreateSupplierPaymentCommandHandler(
    ISupplierPaymentCreationStore store,
    ISupplierPaymentCreationAuthorizer authorizer,
    ISupplierPaymentCreationAuditWriter auditWriter)
    : IRequestHandler<CreateSupplierPaymentCommand, SupplierPaymentReadModel>
{
    public async Task<SupplierPaymentReadModel> Handle(
        CreateSupplierPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await authorizer.AuthorizeAsync(request.OutletId, cancellationToken);
        if (!await store.SupplierExistsAsync(scope.BusinessId, request.SupplierId, cancellationToken))
            throw new InvalidOperationException($"Supplier {request.SupplierId} was not found.");
        if (!await store.PaymentAccountExistsAsync(scope.BusinessId, request.PaymentAccountId, cancellationToken))
            throw new InvalidOperationException("Payment account was not found.");

        var paymentDate = request.PaymentDate.Date;
        var paymentMethod = NormalizeRequired(request.PaymentMethod, "Payment method", 40);
        var payment = new SupplierPayment
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
            AllocatedAmount = 0,
            AdvanceAmount = 0,
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

        await store.CreateDraftAsync(payment, cancellationToken);

        var allocationLines = request.Allocations!;
        var allocationTotal = allocationLines.Sum(line => line.Amount);
        if (allocationTotal > request.Amount)
            throw new InvalidOperationException("Allocation total cannot exceed the payment amount.");

        var allocations = new List<SupplierPaymentAllocation>();
        foreach (var line in allocationLines)
        {
            if (line.SupplierPayableDocumentId.HasValue)
            {
                var payable = await store.FindPayableDocumentAsync(
                    scope.BusinessId,
                    request.SupplierId,
                    line.SupplierPayableDocumentId.Value,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Payable document {line.SupplierPayableDocumentId.Value} was not found for supplier {request.SupplierId}.");
                var outstanding = await store.GetPayableOutstandingAsync(payable.Id, cancellationToken);
                if (line.Amount <= 0 || line.Amount > outstanding)
                    throw new InvalidOperationException("Allocation cannot exceed the payable balance.");

                allocations.Add(new SupplierPaymentAllocation
                {
                    SupplierPaymentId = payment.Id,
                    PayableDocumentType = "supplier_payable_document",
                    PayableDocumentId = payable.Id,
                    SupplierPayableDocumentId = payable.Id,
                    AllocatedAmount = line.Amount,
                    OutstandingBeforeAllocation = outstanding,
                    OutstandingAfterAllocation = outstanding - line.Amount,
                    AllocationDate = payment.PaymentDate,
                    Status = "pending",
                    SourceReference = payable.SourceReference,
                    CreatedBy = request.ActorUserId,
                    CreatedAt = DateTime.UtcNow
                });
                continue;
            }

            var bill = await store.FindBillAsync(
                scope.BusinessId,
                request.SupplierId,
                line.BillId,
                cancellationToken);
            if (bill is null)
                throw new InvalidOperationException(
                    $"Bill {line.BillId} was not found for supplier {request.SupplierId}.");

            var outstandingBefore = bill.AmountDue;
            var outstandingAfter = outstandingBefore - line.Amount;
            if (outstandingAfter < 0)
                throw new InvalidOperationException("Allocation cannot exceed the payable balance.");

            allocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = payment.Id,
                PayableDocumentType = "bill",
                PayableDocumentId = bill.Id,
                AllocatedAmount = line.Amount,
                OutstandingBeforeAllocation = outstandingBefore,
                OutstandingAfterAllocation = outstandingAfter,
                AllocationDate = payment.PaymentDate,
                SourceReference = bill.Id.ToString(),
                CreatedBy = request.ActorUserId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await store.CreateAllocationsAsync(allocations, cancellationToken);
        await auditWriter.RecordCreatedAsync(
            new SupplierPaymentCreationAuditRecord(
                payment.Id,
                payment.BusinessId,
                payment.OutletId,
                payment.SupplierId,
                payment.Amount,
                payment.PaymentMethod,
                payment.PaymentAccountId,
                payment.JournalEntryId,
                payment.ReversalJournalEntryId,
                request.ActorUserId,
                allocationLines.Count,
                allocationTotal),
            cancellationToken);
        return await store.FindByIdAsync(payment.Id, cancellationToken);
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
        var reference = string.IsNullOrWhiteSpace(referenceNumber) ? "default" : referenceNumber.Trim();
        return $"{businessId}:{supplierId}:{paymentDate:yyyyMMdd}:{reference}";
    }
}
