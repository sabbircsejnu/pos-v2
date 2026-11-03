namespace RetailPOS.Core.Entities.HR.Organisation;

public sealed class HrOrgUnit
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnitType { get; set; } = "";
    public long? ParentOrgUnitId { get; set; }
    public long? OperationalLocationProjectionId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long UpdatedByUserId { get; set; }
}
