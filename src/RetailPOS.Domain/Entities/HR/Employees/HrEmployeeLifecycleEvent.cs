namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeLifecycleEvent
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public HrEmployee Employee { get; set; } = null!;
    public long EmploymentPeriodId { get; set; }
    public HrEmploymentPeriod EmploymentPeriod { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string PreviousStateJson { get; set; } = "{}";
    public string NewStateJson { get; set; } = "{}";
    public string? Reason { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public long? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
