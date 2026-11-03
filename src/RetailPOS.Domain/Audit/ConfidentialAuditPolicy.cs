namespace RetailPOS.Core.Audit;

public static class ConfidentialAuditPolicy
{
    public const string RedactedToken = "***REDACTED***";

    public static readonly string[] HrEntityTypes =
    [
        "Employee", "EmployeeDocument", "EmployeeLifecycleEvent", "EmployeeLocationAssignment",
        "EmployeeTransfer", "EmployeeUserLink", "EmployeeIdentity", "ConfidentialAuditAccess",
        "ApprovalPolicy", "ApprovalPolicyStep", "ApprovalInstance", "ApprovalInstanceStep", "ApprovalDecision"
    ];

    public static readonly string[] PayrollEntityTypes =
    [
        "SalarySetup", "SalaryRevision", "EmployeeSalary", "EmployeeSalaryHistory",
        "PayrollPeriod", "PayrollRun", "PayrollEmployeeResult", "PayrollPayment",
        "PaymentBatch", "PaymentBatchItem", "Payslip", "PayslipArtifact", "PayrollExport",
        "FinalSettlement", "EmployeeBankAccount", "EmployeeMobilePaymentAccount",
        "ApprovalPolicy", "ApprovalPolicyStep", "ApprovalInstance", "ApprovalInstanceStep", "ApprovalDecision"
    ];

    public static bool IsHr(string module, string? entityType) =>
        module.Equals("hr", StringComparison.OrdinalIgnoreCase) || IsHrEntity(entityType);

    public static bool IsPayroll(string module, string? entityType) =>
        module.Equals("payroll", StringComparison.OrdinalIgnoreCase) || IsPayrollEntity(entityType);

    public static bool IsHrEntity(string? entityType) =>
        entityType is not null && (entityType.StartsWith("hr", StringComparison.OrdinalIgnoreCase)
            || entityType.StartsWith("Employee", StringComparison.OrdinalIgnoreCase)
            || HrEntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase));

    public static bool IsPayrollEntity(string? entityType) =>
        entityType is not null && (entityType.StartsWith("payroll", StringComparison.OrdinalIgnoreCase)
            || entityType.StartsWith("salary", StringComparison.OrdinalIgnoreCase)
            || entityType.StartsWith("payslip", StringComparison.OrdinalIgnoreCase)
            || PayrollEntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase));

    public static bool IsSafeField(string fieldName) => fieldName is
        "Id" or "BusinessId" or "Status" or "Version" or "CreatedAt" or "UpdatedAt" or "RecordCount" or "OutputHash";

    public static string? SafeReference(string? reference) =>
        reference is null || long.TryParse(reference, out _) || Guid.TryParse(reference, out _)
            ? reference : RedactedToken;
}
