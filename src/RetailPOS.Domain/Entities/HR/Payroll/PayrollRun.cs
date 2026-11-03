namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollRun
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPeriodId { get; set; }
    public int RunNo { get; set; }
    public string RunType { get; set; } = PayrollRunTypes.Regular;
    public string? RunReference { get; set; }
    public string? CorrectionReference { get; set; }
    public long? OriginalPayrollRunId { get; set; }
    public int? OriginalCalculationVersion { get; set; }
    public string? OriginalEvidenceHash { get; set; }
    public string? CorrectionType { get; set; }
    public string? CorrectionReason { get; set; }
    public string? AffectedEmployeeIdsSnapshotJson { get; set; }
    public bool DownstreamReversalRequired { get; set; }
    public bool PaymentCorrectionRequired { get; set; }
    public bool AccountingCorrectionRequired { get; set; }
    public string ScopeType { get; set; } = PayrollRunScopeTypes.Business;
    public long? ScopeId { get; set; }
    public string ScopeKey { get; set; } = PayrollRunScopeTypes.BusinessKey;
    public string Status { get; set; } = PayrollRunStatuses.Draft;
    public string? CurrencyCode { get; set; }
    public int? DecimalPrecision { get; set; }
    public string? RoundingMode { get; set; }
    public long? MonetaryConfigurationVersionId { get; set; }
    public DateTime? CalculatedAtUtc { get; set; }
    public int CalculationVersion { get; set; }
    public string? CalculationInputHash { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedTargetVersion { get; set; }
    public string? ApprovalEvidenceHash { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; } = 1;
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PayrollRunEmployeeSelection
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollRunId { get; set; }
    public long EmployeeId { get; set; }
    public string EmployeeCodeSnapshot { get; set; } = "";
    public string EmployeeNameSnapshot { get; set; } = "";
    public long? LocationIdSnapshot { get; set; }
    public long? EmploymentPeriodId { get; set; }
    public DateOnly? EmploymentStartDateSnapshot { get; set; }
    public DateOnly? EmploymentEndDateSnapshot { get; set; }
    public DateOnly EligibilityDate { get; set; }
    public string? EmploymentStatusSnapshot { get; set; }
    public long? SalaryVersionId { get; set; }
    public DateOnly? SalaryEffectiveFromSnapshot { get; set; }
    public bool IsIncluded { get; set; }
    public string? ExclusionReasonCode { get; set; }
    public DateTime SelectedAtUtc { get; set; }
}

public static class PayrollRunTypes
{
    public const string Regular = "Regular";
    public const string Correction = "Correction";
    public const string OffCycle = "OffCycle";

    public static bool IsValid(string runType) =>
        runType is Regular or Correction or OffCycle;
}

public static class PayrollRunScopeTypes
{
    public const string Business = "Business";
    public const string Location = "Location";
    public const string BusinessKey = "BUSINESS";

    public static string LocationKey(long locationId) =>
        $"LOCATION:{locationId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

public static class PayrollRunStatuses
{
    public const string Draft = "Draft";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string PaymentInProgress = "PaymentInProgress";
    public const string Paid = "Paid";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}
