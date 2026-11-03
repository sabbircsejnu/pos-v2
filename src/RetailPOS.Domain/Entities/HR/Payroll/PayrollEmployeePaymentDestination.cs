namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollEmployeePaymentDestination
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public int VersionNo { get; set; }
    public long Version { get; set; } = 1;
    public long OperationalLocationProjectionId { get; set; }
    public string Method { get; set; } = PayrollPaymentDestinationMethods.Cash;
    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public string? BankName { get; set; }
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public string? MobileProvider { get; set; }
    public string? MobileAccountName { get; set; }
    public string? MobileAccountNumber { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public static class PayrollPaymentDestinationMethods
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Mobile = "Mobile";
}
