namespace RetailPOS.Core.Entities.HR.Organisation;

public enum HrPositionStatus
{
    Vacant,
    Filled,
    TemporarilyFilled,
    Inactive
}

public sealed class HrPosition
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long OrgUnitId { get; set; }
    public HrOrgUnit OrgUnit { get; set; } = null!;
    public long? OperationalLocationProjectionId { get; set; }
    public long? ReportsToPositionId { get; set; }
    public HrPositionStatus Status { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long Version { get; set; } = 1;
}
