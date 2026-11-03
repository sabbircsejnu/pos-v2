namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeDocument
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long DocumentTypeId { get; set; }
    public HrDocumentType DocumentType { get; set; } = null!;
    public string? DocumentNumber { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
    public bool IsLegalHold { get; set; }
    public long Version { get; set; } = 1;
}
