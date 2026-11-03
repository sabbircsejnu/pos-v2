namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmploymentPeriod
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public HrEmployee Employee { get; set; } = null!;
    public long EmploymentTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? StartReason { get; set; }
    public string? EndReason { get; set; }
    public string Status { get; set; } = "Active";
    public string SourceEventType { get; set; } = "Joined";
}
