using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollPaymentBatchItemInput(long PaymentId, long ExpectedVersion);

public sealed record CreatePayrollPaymentBatchRequest(
    string BatchReference,
    string IdempotencyKey,
    IReadOnlyList<PayrollPaymentBatchItemInput> Payments);

public sealed record PayrollPaymentBatchItemResponse(
    long PaymentId,
    long EmployeeId,
    decimal Amount,
    string Status,
    long Version,
    string Method);

public sealed record PayrollPaymentBankFileVersionResponse(
    int VersionNo,
    string ContentHash,
    int RowCount,
    decimal ExpectedAmount,
    string FileName,
    long GeneratedByUserId,
    DateTime GeneratedAtUtc,
    string GenerationReason);

public sealed record PayrollPaymentBatchResponse(
    long Id,
    long BusinessId,
    long PayrollRunId,
    long PayrollPeriodId,
    string BatchReference,
    string Method,
    string CurrencyCode,
    decimal ExpectedAmount,
    string Status,
    long Version,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    IReadOnlyList<PayrollPaymentBatchItemResponse> Payments,
    IReadOnlyList<PayrollPaymentBankFileVersionResponse> BankFileVersions,
    bool IsReplay = false,
    DateTime? SubmittedAtUtc = null,
    long? SubmittedByUserId = null);

public sealed record PayrollPaymentBatchSelectionScope(
    long PaymentId,
    long LocationId,
    string Method);

public sealed record PayrollPaymentBankFileRequest(
    long ExpectedVersion,
    string IdempotencyKey,
    string Reason);

public sealed record SubmitPayrollPaymentBatchRequest(
    long ExpectedVersion,
    string IdempotencyKey,
    string ExternalReference,
    string Reason);

public sealed record PayrollPaymentBankFileResult(
    byte[] Content,
    string FileName,
    string ContentType,
    string ContentHash,
    int VersionNo,
    int RowCount,
    decimal ExpectedAmount,
    bool IsReplay);

public sealed record PayrollPaymentBankFileDraft(
    long BusinessId,
    long PaymentBatchId,
    long ExpectedBatchVersion,
    string IdempotencyKey,
    string PayloadHash,
    string ContentHash,
    byte[] Content,
    string FileName,
    string ContentType,
    int VersionNo,
    int RowCount,
    decimal ExpectedAmount,
    string Reason);

public interface IPayrollPaymentBatchStore
{
    Task<IReadOnlyList<PayrollPaymentBatchSelectionScope>> GetPaymentScopesAsync(
        long businessId, IReadOnlyList<long> paymentIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> GetBatchLocationIdsAsync(
        long businessId, long batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollPaymentBatchResponse>> ListAsync(
        long businessId, long? payrollRunId, CancellationToken cancellationToken);

    Task<PayrollPaymentBatchResponse> GetAsync(
        long businessId, long batchId, CancellationToken cancellationToken);

    Task<PayrollPaymentBatchResponse> CreateAsync(
        long businessId, long actorUserId, long payrollRunId,
        CreatePayrollPaymentBatchRequest request, string payloadHash,
        CancellationToken cancellationToken);
    Task<PayrollPaymentBatchResponse> SubmitAsync(
        long businessId, long actorUserId, long batchId,
        SubmitPayrollPaymentBatchRequest request, string payloadHash,
        CancellationToken cancellationToken);

    Task<PayrollPaymentBankFileDraft> PrepareBankFileAsync(
        long businessId, long batchId, PayrollPaymentBankFileRequest request,
        string normalizedReason, CancellationToken cancellationToken);

    Task<PayrollPaymentBankFileResult> PersistBankFileAsync(
        long businessId, long actorUserId, PayrollPaymentBankFileDraft draft,
        CancellationToken cancellationToken);
}

public sealed record CreatePayrollPaymentBatchCommand(
    long PayrollRunId, CreatePayrollPaymentBatchRequest Data)
    : IRequest<PayrollPaymentBatchResponse>;

public sealed record GetPayrollPaymentBatchQuery(long BatchId)
    : IRequest<PayrollPaymentBatchResponse>;

public sealed record ListPayrollPaymentBatchesQuery(long? PayrollRunId)
    : IRequest<IReadOnlyList<PayrollPaymentBatchResponse>>;

public sealed record GeneratePayrollPaymentBankFileCommand(
    long BatchId, PayrollPaymentBankFileRequest Data)
    : IRequest<PayrollPaymentBankFileResult>;

public sealed record SubmitPayrollPaymentBatchCommand(
    long BatchId, SubmitPayrollPaymentBatchRequest Data)
    : IRequest<PayrollPaymentBatchResponse>;

public sealed class PayrollPaymentBatchHandlers(
    IPayrollPaymentAccess access,
    IPayrollPaymentBatchStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollPaymentBatchCommand, PayrollPaymentBatchResponse>,
    IRequestHandler<GetPayrollPaymentBatchQuery, PayrollPaymentBatchResponse>,
    IRequestHandler<ListPayrollPaymentBatchesQuery, IReadOnlyList<PayrollPaymentBatchResponse>>,
    IRequestHandler<GeneratePayrollPaymentBankFileCommand, PayrollPaymentBankFileResult>,
    IRequestHandler<SubmitPayrollPaymentBatchCommand, PayrollPaymentBatchResponse>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PayrollPaymentBatchResponse> Handle(
        CreatePayrollPaymentBatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.PayrollRunId <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.PayrollRunId));
        var request = NormalizeAndValidate(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureReadPermissionAsync(businessId, cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.process", cancellationToken);

        var scopes = await store.GetPaymentScopesAsync(
            businessId, request.Payments.Select(row => row.PaymentId).ToArray(), cancellationToken);
        if (scopes.Count != request.Payments.Count)
            throw new KeyNotFoundException("A selected Payroll payment was not found.");
        foreach (var scope in scopes)
            await access.EnsureLocationAccessibleAsync(
                businessId, scope.LocationId, cancellationToken);
        if (scopes.Any(scope => scope.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);

        var payloadHash = HashPayload(businessId, command.PayrollRunId, request);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "payroll.payment_batch.created",
                "Payroll",
                "Created a ready payment batch from an approved Payroll run.",
                "PayrollRun",
                command.PayrollRunId.ToString(CultureInfo.InvariantCulture),
                request.Payments.Select(item => new AuditEventEntityWriteRequest(
                    "PayrollPayment",
                    item.PaymentId.ToString(CultureInfo.InvariantCulture),
                    "included")).ToArray()),
            token => store.CreateAsync(
                businessId, actorUserId, command.PayrollRunId, request, payloadHash, token),
            result => !result.IsReplay,
            cancellationToken);
        await EnsureBatchLocationsAsync(businessId, response.Id, cancellationToken);
        return response;
    }

    public async Task<PayrollPaymentBatchResponse> Handle(
        GetPayrollPaymentBatchQuery query, CancellationToken cancellationToken)
    {
        if (query.BatchId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.BatchId));
        var businessId = access.RequireBusinessId();
        await EnsureReadPermissionAsync(businessId, cancellationToken);
        await EnsureBatchLocationsAsync(businessId, query.BatchId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "payroll.payment_batch.accessed",
                "Payroll",
                "Read Payroll payment batch details.",
                "PayrollPaymentBatch",
                query.BatchId.ToString(CultureInfo.InvariantCulture)),
            token => store.GetAsync(businessId, query.BatchId, token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollPaymentBatchResponse>> Handle(
        ListPayrollPaymentBatchesQuery query, CancellationToken cancellationToken)
    {
        if (query.PayrollRunId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.PayrollRunId));
        var businessId = access.RequireBusinessId();
        await EnsureReadPermissionAsync(businessId, cancellationToken);
        var batches = await store.ListAsync(businessId, query.PayrollRunId, cancellationToken);
        foreach (var batch in batches)
            await EnsureBatchLocationsAsync(businessId, batch.Id, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "payroll.payment_batch.list_accessed",
                "Payroll",
                "Read Payroll payment batch summaries.",
                "PayrollRun",
                query.PayrollRunId?.ToString(CultureInfo.InvariantCulture)),
            _ => Task.FromResult(batches),
            cancellationToken);
    }

    public async Task<PayrollPaymentBankFileResult> Handle(
        GeneratePayrollPaymentBankFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.BatchId <= 0 || command.Data.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.BatchId));
        var request = NormalizeBankFileRequest(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureReadPermissionAsync(businessId, cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.bank_file.generate", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view_sensitive", cancellationToken);
        await EnsureBatchLocationsAsync(businessId, command.BatchId, cancellationToken);

        var draft = await store.PrepareBankFileAsync(
            businessId, command.BatchId, request, request.Reason, cancellationToken);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "payroll.bank_file.generated",
                "Payroll",
                "Generated a versioned bank payment file from frozen payment destination snapshots.",
                "PayrollPaymentBatch",
                command.BatchId.ToString(CultureInfo.InvariantCulture),
                [new AuditEventEntityWriteRequest(
                    "PayrollBankFile",
                    command.BatchId.ToString(CultureInfo.InvariantCulture),
                    "generated",
                    JsonSerializer.Serialize(new
                    {
                        draft.VersionNo,
                        draft.ContentHash,
                        draft.RowCount,
                        draft.ExpectedAmount,
                        draft.IdempotencyKey,
                        draft.Reason
                    }, JsonOptions))]),
            token => store.PersistBankFileAsync(
                businessId, actorUserId, draft, token),
            cancellationToken);
        return response;
    }

    public async Task<PayrollPaymentBatchResponse> Handle(
        SubmitPayrollPaymentBatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.BatchId <= 0 || command.Data.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.BatchId));
        var request = NormalizeSubmissionRequest(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureReadPermissionAsync(businessId, cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.process", cancellationToken);
        await EnsureBatchLocationsAsync(businessId, command.BatchId, cancellationToken);
        var batch = await store.GetAsync(businessId, command.BatchId, cancellationToken);
        if (batch.Payments.Any(payment =>
                payment.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);
        var payloadHash = HashPayload(businessId, command.BatchId, request);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "payroll.payment_batch.submitted",
                "Payroll",
                "Recorded external submission of a ready Payroll payment batch.",
                "PayrollPaymentBatch",
                command.BatchId.ToString(CultureInfo.InvariantCulture),
                [new AuditEventEntityWriteRequest(
                    "PayrollPaymentBatch",
                    command.BatchId.ToString(CultureInfo.InvariantCulture),
                    "submitted",
                    JsonSerializer.Serialize(new
                    {
                        request.ExternalReference,
                        request.Reason,
                        request.IdempotencyKey
                    }, JsonOptions))]),
            token => store.SubmitAsync(
                businessId, actorUserId, command.BatchId, request, payloadHash, token),
            cancellationToken);
    }

    private async Task EnsureReadPermissionAsync(
        long businessId, CancellationToken cancellationToken)
    {
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
    }

    private async Task EnsureBatchLocationsAsync(
        long businessId, long batchId, CancellationToken cancellationToken)
    {
        var locations = await store.GetBatchLocationIdsAsync(
            businessId, batchId, cancellationToken);
        foreach (var locationId in locations)
            await access.EnsureLocationAccessibleAsync(
                businessId, locationId, cancellationToken);
    }

    private static CreatePayrollPaymentBatchRequest NormalizeAndValidate(
        CreatePayrollPaymentBatchRequest request)
    {
        var batchReference = request.BatchReference?.Trim();
        if (string.IsNullOrWhiteSpace(batchReference) || batchReference.Length > 100
            || batchReference.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException(
                "Batch reference must contain only letters, digits, hyphens, or underscores.");
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 150)
            throw new ArgumentException("A valid batch idempotency key is required.");
        if (request.Payments is null || request.Payments.Count is < 1 or > 500
            || request.Payments.Any(item => item.PaymentId <= 0 || item.ExpectedVersion <= 0)
            || request.Payments.Select(item => item.PaymentId).Distinct().Count()
                != request.Payments.Count)
            throw new ArgumentException(
                "A batch must contain 1 to 500 unique payment IDs with positive expected versions.");
        return request with
        {
            BatchReference = batchReference,
            IdempotencyKey = idempotencyKey,
            Payments = request.Payments.OrderBy(item => item.PaymentId).ToArray()
        };
    }

    private static PayrollPaymentBankFileRequest NormalizeBankFileRequest(
        PayrollPaymentBankFileRequest request)
    {
        var key = request.IdempotencyKey?.Trim();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 150)
            throw new ArgumentException("A valid bank-file idempotency key is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ArgumentException("A reason is required for each bank-file generation.");
        return request with { IdempotencyKey = key, Reason = reason };
    }

    private static SubmitPayrollPaymentBatchRequest NormalizeSubmissionRequest(
        SubmitPayrollPaymentBatchRequest request)
    {
        var key = request.IdempotencyKey?.Trim();
        var reason = request.Reason?.Trim();
        var reference = request.ExternalReference?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 150)
            throw new ArgumentException("A valid batch-submission idempotency key is required.");
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 150)
            throw new ArgumentException("An external submission reference is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ArgumentException("A reason is required for batch submission.");
        return request with
        {
            IdempotencyKey = key,
            ExternalReference = reference,
            Reason = reason
        };
    }

    private static string HashPayload(
        long businessId, long payrollRunId, CreatePayrollPaymentBatchRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new { businessId, payrollRunId, request.BatchReference, request.Payments },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string HashPayload(
        long businessId, long batchId, SubmitPayrollPaymentBatchRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new { businessId, batchId, request.ExpectedVersion, request.IdempotencyKey,
                request.ExternalReference, request.Reason },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
