using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollPaymentAllocationInput(
    string Method,
    decimal Amount,
    string? ExternalReference = null);

public sealed record PayrollPaymentAllocationEvidenceInput(int AllocationNo, decimal Amount);

public sealed record CreatePayrollPaymentRequest(
    long PayrollEmployeeResultId,
    long ExpectedRunVersion,
    string IdempotencyKey,
    IReadOnlyList<PayrollPaymentAllocationInput> Allocations,
    string? Notes = null);

public sealed record CreateFinalSettlementPaymentRequest(
    int RevisionNo,
    string IdempotencyKey,
    IReadOnlyList<PayrollPaymentAllocationInput> Allocations,
    string? Notes = null);

public sealed record PayrollPaymentAllocationResponse(
    long Id,
    int AllocationNo,
    string Method,
    decimal Amount,
    decimal SettledAmount,
    string State,
    string? ExternalReference,
    long? PaymentDestinationId,
    int? PaymentDestinationVersionNo,
    string? BankName,
    string? AccountName,
    string? AccountNumber,
    string? MobileProvider,
    string? MobileAccountName,
    string? MobileAccountNumber);

public sealed record PayrollPaymentEvidenceResponse(
    long Id,
    string Type,
    decimal Amount,
    DateOnly EvidenceDate,
    string EvidenceReference,
    long VerifiedByUserId,
    DateTime VerifiedAtUtc,
    IReadOnlyList<PayrollPaymentEvidenceAllocationResponse> Allocations);

public sealed record PayrollPaymentEvidenceAllocationResponse(
    int AllocationNo,
    string Method,
    decimal Amount);

public sealed record PayrollPaymentResponse(
    long Id,
    long BusinessId,
    long? PayrollPeriodId,
    long? PayrollRunId,
    long? PayrollEmployeeResultId,
    long EmployeeId,
    long? LocationIdSnapshot,
    decimal PayableAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string CurrencyCode,
    string Method,
    string Status,
    long Version,
    int AttemptNo,
    long? OriginalPaymentId,
    string IdempotencyKey,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    string? Notes,
    IReadOnlyList<PayrollPaymentAllocationResponse> Allocations,
    DateTime? PaidAtUtc,
    string? ExternalReference,
    string? FailureReturnReason,
    long? HeldByUserId,
    DateTime? HeldAtUtc,
    string? HoldReason,
    long? ReleasedByUserId,
    DateTime? ReleasedAtUtc,
    string? ReleaseReason,
    string ResolutionStatus,
    string? ResolutionEvidenceReference,
    IReadOnlyList<PayrollPaymentEvidenceResponse> Evidence,
    bool IsReplay = false,
    long? PaymentBatchId = null,
    long? FinalSettlementId = null,
    int? FinalSettlementRevisionNo = null,
    long? ApprovalInstanceId = null);

public sealed record PayrollPaymentStateChangeRequest(long ExpectedVersion, string Reason);

public sealed record PayrollPaymentFailureRequest(
    long ExpectedVersion, string Reason, string? ExternalReference);

public sealed record PayrollPaymentEvidenceRequest(
    long ExpectedVersion,
    string IdempotencyKey,
    DateOnly EvidenceDate,
    string EvidenceReference,
    IReadOnlyList<PayrollPaymentAllocationEvidenceInput> Allocations,
    string? Reason = null);

public sealed record RetryPayrollPaymentRequest(
    long ExpectedVersion,
    string IdempotencyKey,
    string ResolutionEvidenceReference,
    IReadOnlyList<PayrollPaymentAllocationInput> Allocations,
    string? Notes = null);

public interface IPayrollPaymentAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken);
    Task EnsureLocationAccessibleAsync(
        long businessId, long locationId, CancellationToken cancellationToken);
    Task EnsureFinalSettlementOperationAsync(
        long businessId, HrExitOperation operation, HrExitScope scope,
        CancellationToken cancellationToken);
    bool HasBusinessPermission(string permission);
}

public interface IPayrollPaymentStore
{
    Task<long> GetLocationIdAsync(
        long businessId, long payrollEmployeeResultId, CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> CreateAsync(
        long businessId,
        long actorUserId,
        CreatePayrollPaymentRequest request,
        string idempotencyPayloadHash,
        CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> CreateFinalSettlementAsync(
        long businessId,
        long actorUserId,
        long finalSettlementId,
        CreateFinalSettlementPaymentRequest request,
        string idempotencyPayloadHash,
        CancellationToken cancellationToken);
    Task<HrExitScope?> GetFinalSettlementScopeAsync(
        long businessId, long finalSettlementId, CancellationToken cancellationToken);
    Task<HrExitScope?> GetFinalSettlementPaymentScopeAsync(
        long businessId, long paymentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollPaymentResponse>> GetFinalSettlementPaymentsAsync(
        long businessId, long finalSettlementId, int revisionNo,
        CancellationToken cancellationToken);
    Task<long?> GetFinalSettlementPaymentIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> GetAsync(
        long businessId, long paymentId, CancellationToken cancellationToken);
    Task<long> GetPaymentLocationIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> ChangeStateAsync(
        long businessId, long actorUserId, long paymentId, long expectedVersion,
        string operation, string reason, string? externalReference,
        CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> RecordEvidenceAsync(
        long businessId, long actorUserId, long paymentId,
        PayrollPaymentEvidenceRequest request, string payloadHash,
        string evidenceType, CancellationToken cancellationToken);
    Task<PayrollPaymentResponse> RetryAsync(
        long businessId, long actorUserId, long paymentId,
        RetryPayrollPaymentRequest request, string payloadHash,
        CancellationToken cancellationToken);
}

public sealed record CreatePayrollPaymentCommand(CreatePayrollPaymentRequest Data)
    : IRequest<PayrollPaymentResponse>;

public sealed record CreateFinalSettlementPaymentCommand(
    long FinalSettlementId, CreateFinalSettlementPaymentRequest Data)
    : IRequest<PayrollPaymentResponse>;

public sealed record GetPayrollPaymentQuery(long PaymentId) : IRequest<PayrollPaymentResponse>;

public sealed record ChangePayrollPaymentStateCommand(
    long PaymentId, long ExpectedVersion, string Operation, string Reason,
    string? ExternalReference) : IRequest<PayrollPaymentResponse>;

public sealed record RecordPayrollPaymentEvidenceCommand(
    long PaymentId, PayrollPaymentEvidenceRequest Data, string EvidenceType)
    : IRequest<PayrollPaymentResponse>;

public sealed record RetryPayrollPaymentCommand(
    long PaymentId, RetryPayrollPaymentRequest Data) : IRequest<PayrollPaymentResponse>;

public sealed class PayrollPaymentHandlers(
    IPayrollPaymentAccess access,
    IPayrollPaymentStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollPaymentCommand, PayrollPaymentResponse>,
    IRequestHandler<CreateFinalSettlementPaymentCommand, PayrollPaymentResponse>,
    IRequestHandler<GetPayrollPaymentQuery, PayrollPaymentResponse>,
    IRequestHandler<ChangePayrollPaymentStateCommand, PayrollPaymentResponse>,
    IRequestHandler<RecordPayrollPaymentEvidenceCommand, PayrollPaymentResponse>,
    IRequestHandler<RetryPayrollPaymentCommand, PayrollPaymentResponse>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PayrollPaymentResponse> Handle(
        CreatePayrollPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        var request = NormalizeAndValidate(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.process", cancellationToken);
        if (request.Allocations.Any(allocation =>
                allocation.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);

        var locationId = await store.GetLocationIdAsync(
            businessId, request.PayrollEmployeeResultId, cancellationToken);
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var hash = ComputePayloadHash(businessId, request);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.created", "Payroll",
                "Recorded a Payroll payment attempt from an approved Payroll result.",
                "PayrollEmployeeResult", request.PayrollEmployeeResultId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CreateAsync(businessId, actorUserId, request, hash, token),
            result => !result.IsReplay,
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    public async Task<PayrollPaymentResponse> Handle(
        CreateFinalSettlementPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.FinalSettlementId <= 0 || command.Data.RevisionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.FinalSettlementId));
        var request = NormalizeFinalSettlementRequest(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureMutationPermissionsAsync(
            businessId, "payroll.payment.process", cancellationToken);
        var scope = await store.GetFinalSettlementScopeAsync(
            businessId, command.FinalSettlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        await access.EnsureFinalSettlementOperationAsync(
            businessId, HrExitOperation.FinalSettlementPaymentCreate, scope, cancellationToken);
        if (scope.PrimaryLocationProjectionId is not long locationId)
            throw new UnauthorizedAccessException(
                "Final Settlement payment location scope is unavailable.");
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        if (request.Allocations.Any(allocation =>
                allocation.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);

        var hash = ComputePayloadHash(businessId, command.FinalSettlementId, request);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.final_settlement.payment.created",
                "Payroll", "Created a payment attempt against approved Final Settlement evidence.",
                "PayrollFinalSettlement", command.FinalSettlementId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CreateFinalSettlementAsync(
                businessId, actorUserId, command.FinalSettlementId, request, hash, token),
            result => !result.IsReplay,
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    public async Task<PayrollPaymentResponse> Handle(
        GetPayrollPaymentQuery query, CancellationToken cancellationToken)
    {
        if (query.PaymentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.PaymentId));
        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
        await EnsureFinalSettlementPaymentAccessAsync(
            businessId, query.PaymentId, HrExitOperation.FinalSettlementPaymentView,
            cancellationToken);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.accessed", "Payroll",
                "Read confidential Payroll payment evidence.", "PayrollPayment",
                query.PaymentId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.GetAsync(businessId, query.PaymentId, token),
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    public async Task<PayrollPaymentResponse> Handle(
        ChangePayrollPaymentStateCommand command, CancellationToken cancellationToken)
    {
        if (command.PaymentId <= 0 || command.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.PaymentId));
        var operation = command.Operation?.Trim();
        var permission = operation switch
        {
            "Hold" => "payroll.payment.hold",
            "Release" => "payroll.payment.release",
            "Process" or "Fail" => "payroll.payment.process",
            _ => throw new ArgumentException("Payroll payment action is invalid.")
        };
        var reason = NormalizeRequiredText(command.Reason, 1000, "A reason is required.");
        var externalReference = NormalizeOptionalText(command.ExternalReference, 150);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureMutationPermissionsAsync(businessId, permission, cancellationToken);
        await EnsureFinalSettlementPaymentAccessAsync(
            businessId, command.PaymentId, operation switch
            {
                "Hold" => HrExitOperation.FinalSettlementPaymentHold,
                "Release" => HrExitOperation.FinalSettlementPaymentRelease,
                "Process" => HrExitOperation.FinalSettlementPaymentProcess,
                "Fail" => HrExitOperation.FinalSettlementPaymentFail,
                _ => throw new ArgumentException("Payroll payment action is invalid.")
            }, cancellationToken);
        var locationId = await store.GetPaymentLocationIdAsync(
            businessId, command.PaymentId, cancellationToken);
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var action = operation switch
        {
            "Hold" => "payroll.payment.held",
            "Release" => "payroll.payment.released",
            "Process" => "payroll.payment.processing",
            "Fail" => "payroll.payment.failed",
            _ => throw new ArgumentException("Payroll payment action is invalid.")
        };
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, action, "Payroll",
                $"Recorded a Payroll payment {operation?.ToLowerInvariant()} action.",
                "PayrollPayment", command.PaymentId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ChangeStateAsync(
                businessId, actorUserId, command.PaymentId, command.ExpectedVersion,
                operation!, reason, externalReference, token),
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    public async Task<PayrollPaymentResponse> Handle(
        RecordPayrollPaymentEvidenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.PaymentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.PaymentId));
        var evidenceType = command.EvidenceType?.Trim();
        if (evidenceType is not (PayrollPaymentEvidenceTypes.Settlement
            or PayrollPaymentEvidenceTypes.Return))
            throw new ArgumentException("Payroll payment evidence type is invalid.");
        var request = NormalizeEvidenceRequest(command.Data, evidenceType);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureFinalSettlementPaymentAccessAsync(
            businessId, command.PaymentId,
            evidenceType == PayrollPaymentEvidenceTypes.Return
                ? HrExitOperation.FinalSettlementPaymentReturn
                : HrExitOperation.FinalSettlementPaymentProcess,
            cancellationToken);
        await EnsureMutationPermissionsAsync(
            businessId, "payroll.payment.process", cancellationToken);
        var locationId = await store.GetPaymentLocationIdAsync(
            businessId, command.PaymentId, cancellationToken);
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var current = await store.GetAsync(businessId, command.PaymentId, cancellationToken);
        if (current.Allocations.Any(allocation =>
                allocation.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);
        var hash = ComputePayloadHash(businessId, command.PaymentId, evidenceType, request);
        var action = evidenceType == PayrollPaymentEvidenceTypes.Settlement
            ? "payroll.payment.settled"
            : "payroll.payment.returned";
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, action, "Payroll",
                evidenceType == PayrollPaymentEvidenceTypes.Settlement
                    ? "Recorded operator-verified Payroll payment settlement evidence."
                    : "Recorded operator-verified Payroll payment return evidence.",
                "PayrollPayment", command.PaymentId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.RecordEvidenceAsync(
                businessId, actorUserId, command.PaymentId, request, hash,
                evidenceType, token),
            result => !result.IsReplay,
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    public async Task<PayrollPaymentResponse> Handle(
        RetryPayrollPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.PaymentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.PaymentId));
        var request = NormalizeRetryRequest(command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsureFinalSettlementPaymentAccessAsync(
            businessId, command.PaymentId,
            HrExitOperation.FinalSettlementPaymentRetry, cancellationToken);
        await EnsureMutationPermissionsAsync(
            businessId, "payroll.payment.retry", cancellationToken);
        if (request.Allocations.Any(allocation =>
                allocation.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile))
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.payment.view_sensitive", cancellationToken);
        var locationId = await store.GetPaymentLocationIdAsync(
            businessId, command.PaymentId, cancellationToken);
        await access.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
        var hash = ComputePayloadHash(businessId, command.PaymentId, request);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment.retried", "Payroll",
                "Created a linked Payroll payment reissue after resolution evidence.",
                "PayrollPayment", command.PaymentId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.RetryAsync(
                businessId, actorUserId, command.PaymentId, request, hash, token),
            result => !result.IsReplay,
            cancellationToken);
        await EnsureLocationAccessAsync(businessId, response, cancellationToken);
        return MaskSensitiveFields(response);
    }

    private async Task EnsureMutationPermissionsAsync(
        long businessId, string permission, CancellationToken cancellationToken)
    {
        await access.EnsureBusinessPermissionAsync(
            businessId, permission, cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.payment.view", cancellationToken);
    }

    private async Task EnsureFinalSettlementPaymentAccessAsync(
        long businessId,
        long paymentId,
        HrExitOperation operation,
        CancellationToken cancellationToken)
    {
        var settlementId = await store.GetFinalSettlementPaymentIdAsync(
            businessId, paymentId, cancellationToken);
        if (settlementId is not long id)
            return;
        var scope = await store.GetFinalSettlementPaymentScopeAsync(
            businessId, paymentId, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement payment was not found.");
        await access.EnsureFinalSettlementOperationAsync(
            businessId, operation, scope, cancellationToken);
    }

    private async Task EnsureLocationAccessAsync(
        long businessId, PayrollPaymentResponse response, CancellationToken cancellationToken)
    {
        if (response.LocationIdSnapshot is not > 0)
            throw new UnauthorizedAccessException("Payroll payment location scope is unavailable.");
        await access.EnsureLocationAccessibleAsync(
            businessId, response.LocationIdSnapshot.Value, cancellationToken);
    }

    private PayrollPaymentResponse MaskSensitiveFields(PayrollPaymentResponse response)
    {
        if (access.HasBusinessPermission("payroll.payment.view_sensitive"))
            return response;
        return response with
        {
            Allocations = response.Allocations.Select(allocation => allocation with
            {
                BankName = null,
                AccountName = null,
                AccountNumber = null,
                MobileProvider = null,
                MobileAccountName = null,
                MobileAccountNumber = null
            }).ToArray()
        };
    }

    private static CreatePayrollPaymentRequest NormalizeAndValidate(
        CreatePayrollPaymentRequest request)
    {
        if (request.PayrollEmployeeResultId <= 0 || request.ExpectedRunVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.PayrollEmployeeResultId));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid idempotency key is required.");
        if (request.Allocations is null || request.Allocations.Count == 0)
            throw new ArgumentException("At least one payment allocation is required.");

        var allocations = request.Allocations.Select(allocation =>
        {
            if (allocation is null)
                throw new ArgumentException("Payment allocations cannot be null.");
            var method = allocation.Method?.Trim();
            if (method is not (PayrollPaymentMethods.Cash or PayrollPaymentMethods.Bank
                or PayrollPaymentMethods.Mobile))
                throw new ArgumentException("Payment allocation method is invalid.");
            if (allocation.Amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(request.Allocations),
                    "Payment allocation amounts must be positive.");
            var reference = string.IsNullOrWhiteSpace(allocation.ExternalReference)
                ? null
                : allocation.ExternalReference.Trim();
            if (reference?.Length > 150)
                throw new ArgumentException("External references must not exceed 150 characters.");
            return new PayrollPaymentAllocationInput(method, allocation.Amount, reference);
        }).ToArray();

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > 1000)
            throw new ArgumentException("Payment notes must not exceed 1000 characters.");
        return request with
        {
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Allocations = allocations,
            Notes = notes
        };
    }

    private static CreateFinalSettlementPaymentRequest NormalizeFinalSettlementRequest(
        CreateFinalSettlementPaymentRequest request)
    {
        if (request.RevisionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.RevisionNo));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid idempotency key is required.");
        if (request.Allocations is null || request.Allocations.Count == 0)
            throw new ArgumentException("At least one payment allocation is required.");

        var allocations = request.Allocations.Select(allocation =>
        {
            if (allocation is null)
                throw new ArgumentException("Payment allocations cannot be null.");
            var method = allocation.Method?.Trim();
            if (!PayrollPaymentMethods.IsAllocationMethod(method ?? ""))
                throw new ArgumentException("Payment allocation method is invalid.");
            if (allocation.Amount <= 0m)
                throw new ArgumentOutOfRangeException(nameof(request.Allocations),
                    "Payment allocation amounts must be positive.");
            var reference = NormalizeOptionalText(allocation.ExternalReference, 150);
            return new PayrollPaymentAllocationInput(method!, allocation.Amount, reference);
        }).ToArray();
        var notes = NormalizeOptionalText(request.Notes, 1000);
        return request with
        {
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Allocations = allocations,
            Notes = notes
        };
    }

    private static string ComputePayloadHash(
        long businessId, CreatePayrollPaymentRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            BusinessId = businessId,
            request.PayrollEmployeeResultId,
            request.ExpectedRunVersion,
            request.Allocations,
            request.Notes
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputePayloadHash(
        long businessId, long finalSettlementId, CreateFinalSettlementPaymentRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            BusinessId = businessId,
            FinalSettlementId = finalSettlementId,
            request.RevisionNo,
            request.Allocations,
            request.Notes
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputePayloadHash(
        long businessId,
        long paymentId,
        string evidenceType,
        PayrollPaymentEvidenceRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            BusinessId = businessId,
            PaymentId = paymentId,
            EvidenceType = evidenceType,
            request.ExpectedVersion,
            request.EvidenceDate,
            request.EvidenceReference,
            request.Reason,
            request.Allocations
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputePayloadHash(
        long businessId, long paymentId, RetryPayrollPaymentRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            BusinessId = businessId,
            PaymentId = paymentId,
            request.ExpectedVersion,
            request.ResolutionEvidenceReference,
            request.Allocations,
            request.Notes
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static PayrollPaymentEvidenceRequest NormalizeEvidenceRequest(
        PayrollPaymentEvidenceRequest request, string evidenceType)
    {
        if (request.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid idempotency key is required.");
        if (request.EvidenceDate == default)
            throw new ArgumentException("A valid evidence date is required.");
        if (request.Allocations is null || request.Allocations.Count == 0)
            throw new ArgumentException("At least one allocation evidence item is required.");
        var allocations = request.Allocations.ToArray();
        if (allocations.Any(item => item is null || item.AllocationNo <= 0 || item.Amount <= 0)
            || allocations.Select(item => item.AllocationNo).Distinct().Count() != allocations.Length)
            throw new ArgumentException(
                "Allocation evidence must contain unique positive allocation numbers and amounts.");
        return request with
        {
            IdempotencyKey = request.IdempotencyKey.Trim(),
            EvidenceReference = NormalizeRequiredText(
                request.EvidenceReference, 150, "An evidence reference is required."),
            Reason = evidenceType == PayrollPaymentEvidenceTypes.Return
                ? NormalizeRequiredText(request.Reason, 1000, "A return reason is required.")
                : NormalizeOptionalText(request.Reason, 1000),
            Allocations = allocations
        };
    }

    private static RetryPayrollPaymentRequest NormalizeRetryRequest(
        RetryPayrollPaymentRequest request)
    {
        if (request.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid idempotency key is required.");
        var allocations = NormalizeAllocations(request.Allocations);
        var notes = NormalizeOptionalText(request.Notes, 1000);
        return request with
        {
            IdempotencyKey = request.IdempotencyKey.Trim(),
            ResolutionEvidenceReference = NormalizeRequiredText(
                request.ResolutionEvidenceReference, 150,
                "Resolution evidence is required before a payment can be retried."),
            Allocations = allocations,
            Notes = notes
        };
    }

    private static IReadOnlyList<PayrollPaymentAllocationInput> NormalizeAllocations(
        IReadOnlyList<PayrollPaymentAllocationInput> source)
    {
        if (source is null || source.Count == 0)
            throw new ArgumentException("At least one payment allocation is required.");
        return source.Select(allocation =>
        {
            if (allocation is null)
                throw new ArgumentException("Payment allocations cannot be null.");
            var method = allocation.Method?.Trim();
            if (method is not (PayrollPaymentMethods.Cash or PayrollPaymentMethods.Bank
                or PayrollPaymentMethods.Mobile))
                throw new ArgumentException("Payment allocation method is invalid.");
            if (allocation.Amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(source),
                    "Payment allocation amounts must be positive.");
            var reference = NormalizeOptionalText(allocation.ExternalReference, 150);
            return new PayrollPaymentAllocationInput(method, allocation.Amount, reference);
        }).ToArray();
    }

    private static string NormalizeRequiredText(string? value, int maxLength, string error)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(error);
        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new ArgumentException("A text field exceeds its maximum length.");
        return normalized;
    }
}
