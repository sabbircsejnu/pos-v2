namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeUserLink
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long IdentityUserId { get; set; }
    public DateTime LinkedAtUtc { get; set; }
    public long LinkedByUserId { get; set; }
    public string LinkReason { get; set; } = string.Empty;
    public DateTime? UnlinkedAtUtc { get; set; }
    public long? UnlinkedByUserId { get; set; }
    public string? UnlinkReason { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}
