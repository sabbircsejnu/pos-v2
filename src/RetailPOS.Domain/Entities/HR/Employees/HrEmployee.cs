namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployee
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? GenderCode { get; set; }
    public string? NationalId { get; set; }
    public string? PassportNo { get; set; }
    public string? PersonalEmail { get; set; }
    public string? WorkEmail { get; set; }
    public string? Mobile { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string CurrentEmploymentStatus { get; set; } = "Active";
    public long CurrentEmploymentTypeId { get; set; }
    public HrEmploymentType CurrentEmploymentType { get; set; } = null!;
    public long? CurrentPositionId { get; set; }
    public long? CurrentOrgUnitId { get; set; }
    public long? CurrentPrimaryLocationProjectionId { get; set; }
    public string? SuspendedFromStatus { get; set; }
    public DateOnly JoiningDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public DateOnly? LastWorkingDate { get; set; }
    public long Version { get; set; } = 1;
}
