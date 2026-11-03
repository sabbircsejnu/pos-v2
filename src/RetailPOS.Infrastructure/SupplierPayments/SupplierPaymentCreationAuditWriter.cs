using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPaymentCreationAuditWriter(IAuditService auditService)
    : ISupplierPaymentCreationAuditWriter,
        ISupplierPaymentSubmissionAuditWriter,
    ISupplierPaymentApprovalAuditWriter,
    ISupplierPaymentReversalAuditWriter,
    ISupplierAdvanceCreationAuditWriter,
    ISupplierAdvanceSubmissionAuditWriter,
    ISupplierAdvanceApprovalAuditWriter,
    ISupplierAdvanceUtilizationAuditWriter
{
    public Task RecordAppliedAsync(
    SupplierAdvanceUtilizationAuditRecord record,
    CancellationToken cancellationToken) =>
    auditService.RecordAsync(new AuditEventInput
    {
        ActionType = AuditActionType.Approve,
        Module = AuditModule.Accounts,
        Summary = $"Supplier advance utilized: {record.UtilizationId}",
        PrimaryEntity = ("SupplierAdvanceUtilization", record.UtilizationId.ToString()),
        Metadata = new Dictionary<string, object?>
        {
            ["businessId"] = record.BusinessId,
            ["supplierAdvanceId"] = record.AdvanceId,
            ["supplierPayableDocumentId"] = record.PayableDocumentId,
            ["amount"] = record.Amount,
            ["journalEntryId"] = record.JournalEntryId,
            ["reversalOfUtilizationId"] = null,
            ["actorUserId"] = record.ActorUserId
        },
        Entities =
        [
            new AuditEntityInput(
                "SupplierAdvanceUtilization",
                record.UtilizationId.ToString(),
                AuditOperationType.Update,
                [
                    ("Status", null, "effective"),
                    ("Amount", null, record.Amount),
                    ("JournalEntryId", null, record.JournalEntryId),
                    ("Reason", null, record.Reason)
                ])
        ]
    }, cancellationToken);

    public Task RecordReversedAsync(
    SupplierAdvanceUtilizationReversalAuditRecord record,
    CancellationToken cancellationToken) =>
    auditService.RecordAsync(new AuditEventInput
    {
        ActionType = AuditActionType.Reverse,
        Module = AuditModule.Accounts,
        Summary = $"Supplier advance utilization reversed: {record.UtilizationId}",
        PrimaryEntity = ("SupplierAdvanceUtilization", record.UtilizationId.ToString()),
        Metadata = new Dictionary<string, object?>
        {
            ["businessId"] = record.BusinessId,
            ["supplierAdvanceId"] = record.AdvanceId,
            ["supplierPayableDocumentId"] = record.PayableDocumentId,
            ["amount"] = record.Amount,
            ["journalEntryId"] = record.JournalEntryId,
            ["reversalOfUtilizationId"] = null,
            ["actorUserId"] = record.ActorUserId
        },
        Entities =
        [
            new AuditEntityInput(
                "SupplierAdvanceUtilization",
                record.UtilizationId.ToString(),
                AuditOperationType.Update,
                [
                    ("Status", "effective", "reversed"),
                    ("ReversalJournalEntryId", null, record.ReversalJournalEntryId),
                    ("Reason", null, record.Reason)
                ])
        ]
    }, cancellationToken);

    public Task RecordApprovedAsync(
    SupplierAdvanceApprovalAuditRecord record,
    CancellationToken cancellationToken) =>
    auditService.RecordAsync(new AuditEventInput
    {
        ActionType = AuditActionType.Approve,
        Module = AuditModule.Accounts,
        Summary = $"Supplier advance approved and posted: {record.AdvanceId}",
        PrimaryEntity = ("SupplierAdvance", record.AdvanceId.ToString()),
        Metadata = new Dictionary<string, object?>
        {
            ["businessId"] = record.BusinessId,
            ["outletId"] = record.OutletId,
            ["supplierId"] = record.SupplierId,
            ["amount"] = record.Amount,
            ["remainingAmount"] = record.RemainingAmount,
            ["appliedAmount"] = record.AppliedAmount,
            ["paymentMethod"] = record.PaymentMethod,
            ["paymentAccountId"] = record.PaymentAccountId,
            ["journalEntryId"] = record.JournalEntryId,
            ["sourcePaymentId"] = record.SourcePaymentId,
            ["actorUserId"] = record.ActorUserId
        },
        Entities =
        [
            new AuditEntityInput(
                "SupplierAdvance",
                record.AdvanceId.ToString(),
                AuditOperationType.Update,
                [
                    ("Status", "submitted", "posted"),
                    ("JournalEntryId", null, record.JournalEntryId),
                    ("RemainingAmount", record.Amount, record.RemainingAmount)
                ])
        ]
    }, cancellationToken);

    public Task RecordSubmittedAsync(
        SupplierAdvanceSubmissionAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Submit,
            Module = AuditModule.Accounts,
            Summary = $"Supplier advance submitted: {record.AdvanceId}",
            PrimaryEntity = ("SupplierAdvance", record.AdvanceId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["remainingAmount"] = record.RemainingAmount,
                ["appliedAmount"] = record.AppliedAmount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["sourcePaymentId"] = record.SourcePaymentId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierAdvance",
                    record.AdvanceId.ToString(),
                    AuditOperationType.Update,
                    [("Status", "draft", "submitted")])
            ]
        }, cancellationToken);

    public Task RecordCreatedAsync(
        SupplierAdvanceCreationAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Supplier advance created: {record.AdvanceId}",
            PrimaryEntity = ("SupplierAdvance", record.AdvanceId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["remainingAmount"] = record.RemainingAmount,
                ["appliedAmount"] = record.AppliedAmount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["sourcePaymentId"] = record.SourcePaymentId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierAdvance",
                    record.AdvanceId.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", null, "draft"),
                        ("Amount", null, record.Amount),
                        ("PaymentMethod", null, record.PaymentMethod),
                        ("PaymentAccountId", null, record.PaymentAccountId)
                    ])
            ]
        }, cancellationToken);

    public Task RecordCreatedAsync(
        SupplierPaymentCreationAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Supplier payment created: {record.PaymentId}",
            PrimaryEntity = ("SupplierPayment", record.PaymentId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["reversalJournalEntryId"] = record.ReversalJournalEntryId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierPayment",
                    record.PaymentId.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", null, "draft"),
                        ("Amount", null, record.Amount),
                        ("PaymentMethod", null, record.PaymentMethod),
                        ("PaymentAccountId", null, record.PaymentAccountId),
                        ("AllocationCount", null, record.AllocationCount),
                        ("AllocationTotal", null, record.AllocationTotal)
                    ])
            ]
        }, cancellationToken);

    public Task RecordSubmittedAsync(
        SupplierPaymentSubmissionAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Submit,
            Module = AuditModule.Accounts,
            Summary = $"Supplier payment submitted: {record.PaymentId}",
            PrimaryEntity = ("SupplierPayment", record.PaymentId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["reversalJournalEntryId"] = record.ReversalJournalEntryId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierPayment",
                    record.PaymentId.ToString(),
                    AuditOperationType.Update,
                    [("Status", "draft", "submitted")])
            ]
        }, cancellationToken);

    public Task RecordApprovedAsync(
        SupplierPaymentApprovalAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Approve,
            Module = AuditModule.Accounts,
            Summary = $"Supplier payment approved and posted: {record.PaymentId}",
            PrimaryEntity = ("SupplierPayment", record.PaymentId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["reversalJournalEntryId"] = record.ReversalJournalEntryId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierPayment",
                    record.PaymentId.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", "submitted", "posted"),
                        ("AllocatedAmount", 0m, record.AllocatedAmount),
                        ("AdvanceAmount", 0m, record.AdvanceAmount),
                        ("JournalEntryId", null, record.JournalEntryId),
                        ("AllocationSummary", null, record.AllocationSummary)
                    ])
            ]
        }, cancellationToken);

    public Task RecordReversedAsync(
        SupplierPaymentReversalAuditRecord record,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Reverse,
            Module = AuditModule.Accounts,
            Summary = $"Supplier payment reversed: {record.PaymentId}",
            PrimaryEntity = ("SupplierPayment", record.PaymentId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = record.BusinessId,
                ["outletId"] = record.OutletId,
                ["supplierId"] = record.SupplierId,
                ["amount"] = record.Amount,
                ["paymentMethod"] = record.PaymentMethod,
                ["paymentAccountId"] = record.PaymentAccountId,
                ["journalEntryId"] = record.JournalEntryId,
                ["reversalJournalEntryId"] = record.ReversalJournalEntryId,
                ["actorUserId"] = record.ActorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "SupplierPayment",
                    record.PaymentId.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", "posted", "reversed"),
                        ("ReversalJournalEntryId", null, record.ReversalJournalEntryId),
                        ("ReversalReason", null, record.ReversalReason),
                        ("ReversedAt", null, record.ReversedAt),
                        ("AllocationSummary", null, record.AllocationSummary)
                    ])
            ]
        }, cancellationToken);
}
