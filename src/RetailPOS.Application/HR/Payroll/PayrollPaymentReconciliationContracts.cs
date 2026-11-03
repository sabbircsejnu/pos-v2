using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

/// <summary>Allocation-level settlement evidence for one Payroll payment.</summary>
public sealed record ReconcilePayrollPaymentRequest(
    long ExpectedPaymentVersion,
    long ExpectedReconciliationVersion,
    string IdempotencyKey,
    int AllocationNo,
    decimal SettledAmount,
    DateOnly? SettlementDate,
    string EvidenceSource,
    string? ExternalReference,
    string? Notes = null,
    bool IsException = false);

/// <summary>One allocation's settlement evidence in a payment batch reconciliation.</summary>
public sealed record ReconcilePayrollPaymentBatchItemRequest(
    long PaymentId,
    long ExpectedPaymentVersion,
    int AllocationNo,
    long ExpectedReconciliationVersion,
    decimal SettledAmount,
    DateOnly? SettlementDate,
    string EvidenceSource,
    string? ExternalReference,
    string? Notes = null,
    bool IsException = false);

/// <summary>Allocation-level request to reconcile a payment batch.</summary>
public sealed record ReconcilePayrollPaymentBatchRequest(
    long ExpectedBatchVersion,
    string IdempotencyKey,
    IReadOnlyList<ReconcilePayrollPaymentBatchItemRequest> Items);

/// <summary>Immutable snapshot of one allocation's reconciliation decision.</summary>
public sealed record PayrollPaymentReconciliationResponse(
    long Id,
    long BusinessId,
    long PayrollPaymentId,
    long? PaymentBatchId,
    long EmployeeId,
    long? PayrollPeriodId,
    int AllocationNo,
    decimal ExpectedAmount,
    decimal SettledAmount,
    decimal Difference,
    string Status,
    DateOnly? SettlementDate,
    string EvidenceSource,
    string? ExternalReference,
    string? Notes,
    long Version,
    long PaymentVersion,
    long ReconciledByUserId,
    DateTimeOffset ReconciledAtUtc,
    bool IsReplay = false);

/// <summary>Result of an idempotent payment or batch reconciliation operation.</summary>
public sealed record PayrollPaymentReconciliationOperationResponse(
    IReadOnlyList<PayrollPaymentReconciliationResponse> Items,
    bool IsReplay = false);

public sealed record PayrollPaymentReconciliationTargetInput(
    long PaymentId,
    long ExpectedPaymentVersion,
    int AllocationNo,
    long ExpectedReconciliationVersion,
    decimal SettledAmount,
    DateOnly? SettlementDate,
    string EvidenceSource,
    string? ExternalReference,
    string? Notes,
    bool IsException);

public interface IPayrollPaymentReconciliationStore
{
    Task<long> GetPaymentLocationIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> GetBatchLocationIdsAsync(
        long businessId, long batchId, CancellationToken cancellationToken);

    Task<PayrollPaymentReconciliationOperationResponse> ReconcileAsync(
        long businessId,
        long actorUserId,
        long? batchId,
        long? expectedBatchVersion,
        string idempotencyKey,
        string payloadHash,
        IReadOnlyList<PayrollPaymentReconciliationTargetInput> items,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollPaymentReconciliationResponse>> ListLatestAsync(
        long businessId,
        long? paymentId,
        long? batchId,
        CancellationToken cancellationToken);
}

public sealed record ReconcilePayrollPaymentCommand(
    long PaymentId, ReconcilePayrollPaymentRequest Data)
    : IRequest<PayrollPaymentReconciliationOperationResponse>;

public sealed record ReconcilePayrollPaymentBatchCommand(
    long BatchId, ReconcilePayrollPaymentBatchRequest Data)
    : IRequest<PayrollPaymentReconciliationOperationResponse>;

public sealed record ListPayrollPaymentReconciliationsQuery(
    long? PaymentId, long? PaymentBatchId)
    : IRequest<IReadOnlyList<PayrollPaymentReconciliationResponse>>;

public sealed class PayrollPaymentReconciliationHandlers(
    IPayrollPaymentAccess access,
    IPayrollPaymentReconciliationStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<ReconcilePayrollPaymentCommand, PayrollPaymentReconciliationOperationResponse>,
    IRequestHandler<ReconcilePayrollPaymentBatchCommand, PayrollPaymentReconciliationOperationResponse>,
    IRequestHandler<ListPayrollPaymentReconciliationsQuery,
        IReadOnlyList<PayrollPaymentReconciliationResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PayrollPaymentReconciliationOperationResponse> Handle(
        ReconcilePayrollPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.PaymentId <= 0 || command.Data.ExpectedPaymentVersion <= 0
            || command.Data.ExpectedReconciliationVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(command.PaymentId));
        var idempotencyKey = NormalizeIdempotencyKey(command.Data.IdempotencyKey);
        var item = Normalize(command.PaymentId,
            command.Data with { IdempotencyKey = idempotencyKey });
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureViewAndReconcilePermissionsAsync(
            businessId, item.EvidenceSource != PayrollPaymentMethods.Cash, cancellationToken);
        var locationId = await store.GetPaymentLocationIdAsync(
            businessId, command.PaymentId, cancellationToken);
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var hash = HashPayload(businessId, null, null, idempotencyKey, [item]);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.reconciled", "Payroll",
                "Recorded allocation-level Payroll payment reconciliation evidence.",
                "PayrollPayment", command.PaymentId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ReconcileAsync(
                businessId, actorUserId, null, null, idempotencyKey, hash,
                [item], token),
            result => !result.IsReplay,
            cancellationToken);
        return response;
    }

    public async Task<PayrollPaymentReconciliationOperationResponse> Handle(
        ReconcilePayrollPaymentBatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.BatchId <= 0 || command.Data.ExpectedBatchVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.BatchId));
        var idempotencyKey = NormalizeIdempotencyKey(command.Data.IdempotencyKey);
        if (command.Data.Items is null || command.Data.Items.Count is 0 or > 200)
            throw new ArgumentException("A batch reconciliation must include between 1 and 200 allocation items.");
        var items = command.Data.Items.Select(item =>
        {
            if (item is null || item.PaymentId <= 0 || item.ExpectedPaymentVersion <= 0
                || item.ExpectedReconciliationVersion < 0)
                throw new ArgumentException("Batch reconciliation allocation identity is invalid.");
            return Normalize(item.PaymentId, item.AllocationNo,
                item.ExpectedPaymentVersion, item.ExpectedReconciliationVersion,
                item.SettledAmount, item.SettlementDate, item.EvidenceSource,
                item.ExternalReference, item.Notes, item.IsException, idempotencyKey);
        }).ToArray();
        EnsureUniqueTargets(items);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureViewAndReconcilePermissionsAsync(
            businessId,
            items.Any(item => item.EvidenceSource != PayrollPaymentMethods.Cash),
            cancellationToken);
        var locationIds = await store.GetBatchLocationIdsAsync(
            businessId, command.BatchId, cancellationToken);
        foreach (var locationId in locationIds)
            await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var hash = HashPayload(
            businessId, command.BatchId, command.Data.ExpectedBatchVersion, idempotencyKey, items);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.reconciled", "Payroll",
                "Recorded allocation-level Payroll payment batch reconciliation evidence.",
                "PayrollPaymentBatch", command.BatchId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ReconcileAsync(
                businessId, actorUserId, command.BatchId, command.Data.ExpectedBatchVersion,
                idempotencyKey, hash, items, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollPaymentReconciliationResponse>> Handle(
        ListPayrollPaymentReconciliationsQuery query, CancellationToken cancellationToken)
    {
        if (query.PaymentId is <= 0 || query.PaymentBatchId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.PaymentId));
        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
        var rows = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.reconciliation_accessed",
                "Payroll", "Read confidential Payroll payment reconciliation evidence.",
                "PayrollPaymentReconciliation",
                query.PaymentId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? query.PaymentBatchId?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? "Business"),
            token => store.ListLatestAsync(
                businessId, query.PaymentId, query.PaymentBatchId, token),
            cancellationToken);
        foreach (var paymentId in rows.Select(row => row.PayrollPaymentId).Distinct())
        {
            var locationId = await store.GetPaymentLocationIdAsync(
                businessId, paymentId, cancellationToken);
            await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        }
        return rows;
    }

    private async Task EnsureViewAndReconcilePermissionsAsync(
        long businessId, bool requiresSensitivePermission,
        CancellationToken cancellationToken)
    {
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.reconcile", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
        if (requiresSensitivePermission)
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);
    }

    private static PayrollPaymentReconciliationTargetInput Normalize(
        long paymentId, ReconcilePayrollPaymentRequest request) =>
        Normalize(paymentId, request.AllocationNo, request.ExpectedPaymentVersion,
            request.ExpectedReconciliationVersion, request.SettledAmount,
            request.SettlementDate, request.EvidenceSource, request.ExternalReference,
            request.Notes, request.IsException,
            NormalizeIdempotencyKey(request.IdempotencyKey));

    private static PayrollPaymentReconciliationTargetInput Normalize(
        long paymentId,
        int allocationNo,
        long expectedPaymentVersion,
        long expectedReconciliationVersion,
        decimal settledAmount,
        DateOnly? settlementDate,
        string evidenceSource,
        string? externalReference,
        string? notes,
        bool isException,
        string idempotencyKey)
    {
        if (allocationNo <= 0 || settledAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(allocationNo));
        var source = evidenceSource?.Trim();
        if (source is not (PayrollPaymentMethods.Cash
            or PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            throw new ArgumentException("A valid settlement evidence source is required.");
        var reference = NormalizeOptionalText(externalReference, 150);
        var normalizedNotes = NormalizeOptionalText(notes, 1000);
        if (settledAmount > 0m && (settlementDate is null || reference is null))
            throw new ArgumentException(
                "A positive settled amount requires a settlement date and external reference.");
        if (isException && normalizedNotes is null)
            throw new ArgumentException("An exception reconciliation requires a reason.");
        return new PayrollPaymentReconciliationTargetInput(
            paymentId, expectedPaymentVersion, allocationNo,
            expectedReconciliationVersion, settledAmount, settlementDate, source,
            reference, normalizedNotes, isException);
    }

    private static void EnsureUniqueTargets(
        IReadOnlyList<PayrollPaymentReconciliationTargetInput> items)
    {
        if (items.Select(item => (item.PaymentId, item.AllocationNo)).Distinct().Count()
            != items.Count)
            throw new ArgumentException(
                "A reconciliation request cannot include the same payment allocation twice.");
    }

    private static string NormalizeIdempotencyKey(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 150)
            throw new ArgumentException("A valid reconciliation idempotency key is required.");
        return key;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Reconciliation text must not exceed {maxLength} characters.");
        return normalized;
    }

    private static string HashPayload(
        long businessId,
        long? batchId,
        long? expectedBatchVersion,
        string idempotencyKey,
        IReadOnlyList<PayrollPaymentReconciliationTargetInput> items)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            BusinessId = businessId,
            BatchId = batchId,
            ExpectedBatchVersion = expectedBatchVersion,
            IdempotencyKey = idempotencyKey,
            Items = items.OrderBy(item => item.PaymentId).ThenBy(item => item.AllocationNo)
                .ToArray()
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
