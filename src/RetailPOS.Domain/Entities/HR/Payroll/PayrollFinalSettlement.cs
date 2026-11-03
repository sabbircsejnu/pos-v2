namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollFinalSettlement
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExitCaseId { get; set; }
    public long EmployeeId { get; set; }
    public int CurrentRevisionNo { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
    public List<PayrollFinalSettlementRevision> Revisions { get; set; } = [];
}

public sealed class PayrollFinalSettlementRevision
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long FinalSettlementId { get; set; }
    public int RevisionNo { get; set; }
    public DateOnly SettlementDate { get; set; }
    public string CurrencyCode { get; set; } = "";
    public int DecimalPrecision { get; set; }
    public string RoundingMode { get; set; } = "";
    public decimal GrossDue { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSettlement { get; set; }
    public string CalculationInputHash { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public DateTime CalculatedAtUtc { get; set; }
    public long CalculatedByUserId { get; set; }
    public List<PayrollFinalSettlementLine> Lines { get; set; } = [];
}

public sealed class PayrollFinalSettlementLine
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long RevisionId { get; set; }
    public int LineNo { get; set; }
    public string ComponentCode { get; set; } = "";
    public string ComponentName { get; set; } = "";
    public string LineType { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsApplicable { get; set; }
    public string? NotApplicableReason { get; set; }
    public string SourceType { get; set; } = "";
    public string? SourceId { get; set; }
    public string EvidenceSnapshotJson { get; set; } = "{}";
}

public sealed class PayrollFinalSettlementApprovalSubmission
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long FinalSettlementId { get; set; }
    public long RevisionId { get; set; }
    public int RevisionNo { get; set; }
    public long EmployeeId { get; set; }
    public long ExitCaseId { get; set; }
    public string CalculationSnapshotHash { get; set; } = "";
    public string ComponentLinesHash { get; set; } = "";
    public decimal GrossPayable { get; set; }
    public decimal TotalRecovery { get; set; }
    public decimal NetSettlement { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string SourceEvidenceHash { get; set; } = "";
    public string CalculationPolicyVersion { get; set; } = "";
    public string ClearanceReferencesJson { get; set; } = "[]";
    public string ClearanceReferencesHash { get; set; } = "";
    public long SubmitterUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string SubmittedEvidenceJson { get; set; } = "{}";
    public string SubmittedEvidenceHash { get; set; } = "";
    public long? ApprovalInstanceId { get; set; }
}

public static class PayrollFinalSettlementLineTypes
{
    public const string Earning = "Earning";
    public const string Deduction = "Deduction";
    public const string NotApplicable = "NotApplicable";
}
