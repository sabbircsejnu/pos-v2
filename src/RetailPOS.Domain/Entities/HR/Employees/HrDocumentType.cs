namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrDocumentType
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Classification { get; set; } = "Confidential";
    public bool RequiresExpiryDate { get; set; }
    public int? RetentionPeriodDays { get; set; }
    public bool RequiresLegalHoldReview { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}
