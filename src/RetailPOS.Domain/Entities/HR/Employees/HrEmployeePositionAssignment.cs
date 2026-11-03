namespace RetailPOS.Core.Entities.HR.Employees;

public enum HrEmployeeAssignmentType
{
    Primary,
    Temporary,
    Acting
}

public sealed class HrEmployeePositionAssignment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long PositionId { get; set; }
    public long OrgUnitId { get; set; }
    public long? ReportsToEmployeeId { get; set; }
    public HrEmployeeAssignmentType AssignmentType { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public bool IsCurrent { get; set; }
    public long Version { get; set; } = 1;
}
