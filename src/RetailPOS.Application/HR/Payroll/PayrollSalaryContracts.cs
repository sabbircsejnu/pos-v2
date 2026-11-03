namespace RetailPOS.Application.HR.Payroll;

/// <summary>Configurable earning or deduction component used by salary structures.</summary>
public sealed record PayrollSalaryComponentData(
    string Code,
    string Name,
    string ComponentType,
    string ValueType,
    string? TaxTreatmentCode,
    bool IsRecurring,
    int CalculationOrder,
    string? FormulaExpression);

/// <summary>Salary component configuration scoped to one Business.</summary>
public sealed record PayrollSalaryComponentResponse(
    long Id,
    long BusinessId,
    PayrollSalaryComponentData Data,
    bool IsActive,
    long Version);

/// <summary>A component assignment in a reusable salary structure template.</summary>
public sealed record PayrollSalaryStructureLineData(
    long SalaryComponentId,
    decimal? DefaultValue,
    string? FormulaOverride,
    int CalculationOrder);

/// <summary>A reusable salary structure template; this is not an employee salary approval.</summary>
public sealed record PayrollSalaryStructureData(
    string Code,
    string Name,
    IReadOnlyList<PayrollSalaryStructureLineData> Lines);

/// <summary>A reusable salary structure and its same-Business component lines.</summary>
public sealed record PayrollSalaryStructureResponse(
    long Id,
    long BusinessId,
    PayrollSalaryStructureData Data,
    bool IsActive,
    long Version);

public sealed record PayrollSalarySetupComponentInput(long SalaryComponentId, decimal? Value);

public sealed record PayrollSalarySetupComponentSnapshot(
    long SalaryComponentId,
    string Code,
    string Name,
    string ComponentType,
    string ValueType,
    string? TaxTreatmentCode,
    bool IsRecurring,
    int CalculationOrder,
    string? FormulaExpression,
    decimal? Value);

public sealed record PayrollSalarySetupComponentResponse(
    long SalaryComponentId,
    string Code,
    string Name,
    string ComponentType,
    string ValueType,
    decimal? Value);

public sealed record CreatePayrollSalarySetupRequest(
    long EmployeeId,
    long SalaryStructureId,
    DateOnly EffectiveFrom,
    IReadOnlyList<PayrollSalarySetupComponentInput> Components,
    string Reason,
    string IdempotencyKey);

public sealed record CreatePayrollSalaryRevisionRequest(
    long EmployeeId,
    long SourceSalaryVersionId,
    DateOnly EffectiveFrom,
    IReadOnlyList<PayrollSalarySetupComponentInput> Components,
    string Reason,
    string IdempotencyKey);

public sealed record PayrollSalaryRevisionRequestResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    long SourceSalaryVersionId,
    DateOnly EffectiveFrom,
    long OperationalLocationProjectionId,
    IReadOnlyList<PayrollSalarySetupComponentResponse> CurrentComponents,
    IReadOnlyList<PayrollSalarySetupComponentResponse> ProposedComponents,
    string Reason,
    string Status,
    long? ApprovalInstanceId,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    long? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    long? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    long Version,
    IReadOnlyList<PayrollSalaryApprovalStepResponse> ApprovalSteps,
    IReadOnlyList<PayrollSalaryDecisionResponse> ApprovalHistory,
    bool IsReplay = false);

public sealed record SavePayrollEmployeeAdjustmentRequest(
    Guid? AdjustmentIdentity,
    long? ExpectedVersion,
    long EmployeeId,
    long SalaryComponentId,
    string AdjustmentKind,
    string Classification,
    decimal Amount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Reason,
    string? Reference,
    string IdempotencyKey);

public sealed record EndPayrollEmployeeAdjustmentRequest(
    Guid AdjustmentIdentity,
    long ExpectedVersion,
    DateOnly EffectiveFrom,
    string Reason,
    string IdempotencyKey);

public sealed record PayrollEmployeeAdjustmentResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    Guid AdjustmentIdentity,
    int Version,
    long? PredecessorVersionId,
    long SalaryComponentId,
    string ComponentCode,
    string ComponentName,
    string ComponentType,
    string ValueType,
    string AdjustmentKind,
    string Classification,
    decimal Amount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long OperationalLocationProjectionId,
    string Reason,
    string? Reference,
    string Status,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    bool IsReplay = false);

public interface IPayrollEmployeeAdjustmentStore
{
    Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> EffectiveAsync(
        long businessId, long employeeId, DateOnly asOf, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> HistoryAsync(
        long businessId, Guid adjustmentIdentity, CancellationToken cancellationToken);
    Task<PayrollEmployeeAdjustmentResponse> SaveAsync(
        long businessId, long actorUserId, SavePayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken);
    Task<PayrollEmployeeAdjustmentResponse> EndAsync(
        long businessId, long actorUserId, EndPayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken);
}

public sealed record PayrollSalaryApprovalStepResponse(
    int Sequence,
    string Decision,
    long? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? Comment);

public sealed record PayrollSalaryDecisionResponse(
    string Decision,
    long ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason);

public sealed record PayrollSalarySetupRequestResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    long SalaryStructureId,
    long SalaryStructureVersion,
    DateOnly EffectiveFrom,
    long OperationalLocationProjectionId,
    string StructureCode,
    string StructureName,
    IReadOnlyList<PayrollSalarySetupComponentResponse> Components,
    string Reason,
    string Status,
    long? ApprovalInstanceId,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    long? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    long? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    long Version,
    IReadOnlyList<PayrollSalaryApprovalStepResponse> ApprovalSteps,
    IReadOnlyList<PayrollSalaryDecisionResponse> ApprovalHistory,
    bool IsReplay = false);

public static class PayrollSalarySetupApprovalConstants
{
    public const string TargetType = "PayrollSalarySetupRequest";
}

public static class PayrollSalaryRevisionApprovalConstants
{
    public const string TargetType = "PayrollSalaryRevisionRequest";
}

public sealed record PayrollEmployeeSalaryResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    long SalarySetupRequestId,
    long SalaryStructureId,
    long SalaryStructureVersion,
    DateOnly EffectiveFrom,
    long OperationalLocationProjectionId,
    string StructureCode,
    string StructureName,
    IReadOnlyList<PayrollSalarySetupComponentResponse> Components,
    long ApprovalInstanceId,
    long ApprovedByUserId,
    DateTime ApprovedAtUtc,
    long? SalaryRevisionRequestId = null,
    long? PredecessorSalaryVersionId = null);

public interface IPayrollSalarySetupAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken);
    Task EnsureAsync(
        long businessId,
        long locationProjectionId,
        string permission,
        CancellationToken cancellationToken);
}

public interface IPayrollSalarySetupStore
{
    Task<IReadOnlyList<PayrollSalaryComponentResponse>> ComponentsAsync(
        long businessId, string? search, CancellationToken cancellationToken);
    Task<PayrollSalaryComponentResponse> ComponentAsync(
        long businessId, long id, CancellationToken cancellationToken);
    Task<PayrollSalaryComponentResponse> SaveComponentAsync(
        long businessId, long? id, long? expectedVersion, PayrollSalaryComponentData? data,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollSalaryStructureResponse>> StructuresAsync(
        long businessId, string? search, CancellationToken cancellationToken);
    Task<PayrollSalaryStructureResponse> StructureAsync(
        long businessId, long id, CancellationToken cancellationToken);
    Task<PayrollSalaryStructureResponse> SaveStructureAsync(
        long businessId, long? id, long? expectedVersion, PayrollSalaryStructureData? data,
        CancellationToken cancellationToken);
    Task<long> EmployeePrimaryLocationAsync(
        long businessId, long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken);
    Task<PayrollSalarySetupRequestResponse> CreateInitialRequestAsync(
        long businessId, long actorUserId, CreatePayrollSalarySetupRequest request,
        CancellationToken cancellationToken);
    Task<PayrollSalarySetupRequestResponse> RequestAsync(
        long businessId, long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollSalarySetupRequestResponse>> EmployeeRequestsAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollEmployeeSalaryResponse>> EmployeeSalaryHistoryAsync(
        long businessId, long employeeId, DateOnly? effectiveOn, CancellationToken cancellationToken);
}

public interface IPayrollSalaryRevisionStore
{
    Task<PayrollSalaryRevisionRequestResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollSalaryRevisionRequest request,
        CancellationToken cancellationToken);
    Task<PayrollSalaryRevisionRequestResponse> RequestAsync(
        long businessId, long requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollSalaryRevisionRequestResponse>> EmployeeRequestsAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
}

public sealed record PayrollLoanScheduleLineInput(
    DateOnly DueDate,
    decimal PlannedAmount);

public sealed record CreatePayrollEmployeeLoanRequest(
    long EmployeeId,
    string LoanType,
    decimal PrincipalAmount,
    DateOnly RecoveryStartDate,
    string RecoveryMode,
    decimal? FixedInstallment,
    IReadOnlyList<PayrollLoanScheduleLineInput> Schedule,
    string Reason,
    string? Reference,
    string IdempotencyKey);

public sealed record UpdatePayrollLoanScheduleRequest(
    long ExpectedLoanVersion,
    DateOnly RecoveryStartDate,
    string RecoveryMode,
    decimal? FixedInstallment,
    IReadOnlyList<PayrollLoanScheduleLineInput> Schedule,
    string IdempotencyKey);

public sealed record RecordPayrollLoanRepaymentRequest(
    decimal Amount,
    DateOnly PaymentDate,
    string Reason,
    string? Reference,
    string IdempotencyKey);

public sealed record PayrollLoanScheduleLineResponse(
    int InstallmentNo,
    DateOnly DueDate,
    decimal PlannedAmount,
    decimal ActualRecoveredAmount,
    string Status);

public sealed record PayrollLoanScheduleVersionResponse(
    long Id,
    int VersionNo,
    DateOnly RecoveryStartDate,
    string RecoveryMode,
    decimal? FixedInstallment,
    string Status,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    IReadOnlyList<PayrollLoanScheduleLineResponse> Lines);

public sealed record PayrollLoanRepaymentResponse(
    long Id,
    decimal Amount,
    DateOnly PaymentDate,
    string? Reference,
    string Reason,
    long RecordedByUserId,
    DateTime RecordedAtUtc,
    bool IsReplay = false);

public sealed record PayrollEmployeeLoanResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    long OperationalLocationProjectionId,
    string LoanType,
    decimal PrincipalAmount,
    decimal? ApprovedAmount,
    DateOnly? DisbursedDate,
    DateOnly RecoveryStartDate,
    string RecoveryMode,
    decimal? FixedInstallment,
    decimal OutstandingAmount,
    string Status,
    string Reason,
    string? Reference,
    long? ApprovalInstanceId,
    long CreatedByUserId,
    DateTime CreatedAtUtc,
    long? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    long? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    long Version,
    IReadOnlyList<PayrollSalaryApprovalStepResponse> ApprovalSteps,
    IReadOnlyList<PayrollSalaryDecisionResponse> ApprovalHistory,
    IReadOnlyList<PayrollLoanScheduleVersionResponse> ScheduleHistory,
    IReadOnlyList<PayrollLoanRepaymentResponse> Repayments,
    bool IsReplay = false,
    bool IsOpeningBalance = false,
    DateOnly? OpeningDisbursedDate = null,
    decimal? OpeningBalanceAmount = null);

public static class PayrollEmployeeLoanApprovalConstants
{
    public const string TargetType = "PayrollEmployeeLoan";
}

public interface IPayrollEmployeeLoanStore
{
    Task<PayrollEmployeeLoanResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollEmployeeLoanRequest request,
        CancellationToken cancellationToken);
    Task<PayrollEmployeeLoanResponse> CreateOpeningAsync(
        long businessId, long actorUserId, CreatePayrollEmployeeLoanRequest request,
        DateOnly historicalDisbursedDate, decimal openingBalanceAmount,
        string importIdentity, CancellationToken cancellationToken);
    Task<PayrollEmployeeLoanResponse> RequestAsync(
        long businessId, long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollEmployeeLoanResponse>> EmployeeLoansAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<PayrollEmployeeLoanResponse> UpdateScheduleAsync(
        long businessId, long actorUserId, long loanId, UpdatePayrollLoanScheduleRequest request,
        CancellationToken cancellationToken);
    Task<PayrollEmployeeLoanResponse> RecordRepaymentAsync(
        long businessId, long actorUserId, long loanId, RecordPayrollLoanRepaymentRequest request,
        CancellationToken cancellationToken);
}

public sealed class PayrollSalaryConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
