namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmploymentType
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
