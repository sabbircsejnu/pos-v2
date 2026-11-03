namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollEmployeeResult
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollRunId { get; set; }
    public int CalculationVersion { get; set; }
    public string CalculationInputHash { get; set; } = "";
    public string? CalculationSnapshotJson { get; set; }
    public string? CalculationSnapshotHash { get; set; }
    public long EmployeeId { get; set; }
    public string EmployeeCodeSnapshot { get; set; } = "";
    public string EmployeeNameSnapshot { get; set; } = "";
    public long? LocationIdSnapshot { get; set; }
    public long SalaryVersionId { get; set; }
    public DateOnly SalaryEffectiveFromSnapshot { get; set; }
    public long? CalculationPolicyVersionId { get; set; }
    public string? CalculationPolicySnapshotJson { get; set; }
    public decimal? GrossEarnings { get; set; }
    public decimal? TotalDeductions { get; set; }
    public decimal? NetPay { get; set; }
    public string Status { get; set; } = PayrollEmployeeResultStatuses.Calculated;
    public string? ExceptionCode { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public ICollection<PayrollEmployeeResultLine> Lines { get; set; } = [];
}

public sealed class PayrollEmployeeResultLine
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollEmployeeResultId { get; set; }
    public PayrollEmployeeResult? PayrollEmployeeResult { get; set; }
    public long? SalaryComponentId { get; set; }
    public string ComponentCodeSnapshot { get; set; } = "";
    public string ComponentNameSnapshot { get; set; } = "";
    public string LineType { get; set; } = "";
    public decimal Amount { get; set; }
    public string SourceType { get; set; } = "Salary";
    public string? SourceId { get; set; }
    public long? SourceVersion { get; set; }
    public decimal? InputQuantity { get; set; }
    public decimal? InputRate { get; set; }
    public decimal? InputAmount { get; set; }
    public string? EvidenceCode { get; set; }
    public int CalculationOrder { get; set; }
}

public static class PayrollEmployeeResultStatuses
{
    public const string Calculated = "Calculated";
    public const string Exception = "Exception";
}
