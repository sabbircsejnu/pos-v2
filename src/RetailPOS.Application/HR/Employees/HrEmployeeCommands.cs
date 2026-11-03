using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Employees;

public sealed record CreateHrEmployeeCommand(HrEmployeeCreateData Data) : IRequest<HrEmployeeProfile>;
public sealed record UpdateHrEmployeeCommand(
    long EmployeeId, long ExpectedVersion, HrEmployeeUpdateData Data) : IRequest<HrEmployeeProfile?>;

public sealed class CreateHrEmployeeCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<CreateHrEmployeeCommand, HrEmployeeProfile>
{
    public async Task<HrEmployeeProfile> Handle(
        CreateHrEmployeeCommand request, CancellationToken cancellationToken)
    {
        Validate(request.Data);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.Create, cancellationToken);
        var fieldAccess = access.GetFieldAccess();
        EnsureSensitiveWriteAccess(request.Data, fieldAccess);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "create", "hr", "Create employee",
                "HrEmployee", request.Data.EmployeeCode.Trim()),
            token => store.CreateAsync(businessId, actorUserId, request.Data,
                new HrEmployeeFieldAccess(false, false), token),
            cancellationToken);
    }

    internal static void Validate(HrEmployeeCreateData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidatePerson(data.EmployeeCode, data.FirstName, data.MiddleName, data.LastName, data.DisplayName,
            data.GenderCode, data.NationalId, data.PassportNo, data.PersonalEmail, data.WorkEmail,
            data.Mobile, data.PresentAddress, data.PermanentAddress, data.EmergencyContactName,
            data.EmergencyContactPhone);
        if (data.EmploymentTypeId <= 0)
            throw new ArgumentException("A valid employment type is required.");
        if (data.EmploymentStatus is not ("Active" or "Probation"))
            throw new ArgumentException("Initial employment status must be Active or Probation.");
        if (data.JoiningDate == default)
            throw new ArgumentException("Joining date is required.");
        if (data.ConfirmationDate.HasValue && data.ConfirmationDate.Value < data.JoiningDate)
            throw new ArgumentException("Confirmation date cannot precede joining date.");
    }

    internal static void ValidatePerson(
        string? employeeCode,
        string? firstName,
        string? middleName,
        string? lastName,
        string? displayName,
        string? genderCode,
        string? nationalId,
        string? passportNo,
        string? personalEmail,
        string? workEmail,
        string? mobile,
        string? presentAddress,
        string? permanentAddress,
        string? emergencyContactName,
        string? emergencyContactPhone)
    {
        if (employeeCode is not null)
            Require(employeeCode, 50, "Employee code");
        Require(firstName, 100, "First name");
        Require(displayName, 200, "Display name");
        Limit(middleName, 100, "Middle name");
        Limit(lastName, 100, "Last name");
        Limit(genderCode, 30, "Gender code");
        Limit(nationalId, 100, "National ID");
        Limit(passportNo, 100, "Passport number");
        Limit(personalEmail, 200, "Personal email");
        Limit(workEmail, 200, "Work email");
        Limit(mobile, 50, "Mobile");
        Limit(presentAddress, 4000, "Present address");
        Limit(permanentAddress, 4000, "Permanent address");
        Limit(emergencyContactName, 150, "Emergency contact name");
        Limit(emergencyContactPhone, 50, "Emergency contact phone");
    }

    internal static void EnsureSensitiveWriteAccess(
        HrEmployeeCreateData data, HrEmployeeFieldAccess access)
    {
        if (!access.CanViewSensitive
            && (data.DateOfBirth.HasValue || HasValue(data.GenderCode) || HasValue(data.PersonalEmail)
                || HasValue(data.Mobile) || HasValue(data.PresentAddress) || HasValue(data.PermanentAddress)
                || HasValue(data.EmergencyContactName) || HasValue(data.EmergencyContactPhone)))
            throw new UnauthorizedAccessException("Sensitive employee fields require hr.employee.view_sensitive.");
        if (!access.CanViewIdentity && (HasValue(data.NationalId) || HasValue(data.PassportNo)))
            throw new UnauthorizedAccessException("Identity fields require hr.employee.view_identity.");
    }

    internal static void EnsureSensitiveWriteAccess(
        HrEmployeeUpdateData data, HrEmployeeFieldAccess access)
    {
        if (!access.CanViewSensitive
            && (data.DateOfBirth.HasValue || HasValue(data.GenderCode) || HasValue(data.PersonalEmail)
                || HasValue(data.Mobile) || HasValue(data.PresentAddress) || HasValue(data.PermanentAddress)
                || HasValue(data.EmergencyContactName) || HasValue(data.EmergencyContactPhone)))
            throw new UnauthorizedAccessException("Sensitive employee fields require hr.employee.view_sensitive.");
        if (!access.CanViewIdentity && (HasValue(data.NationalId) || HasValue(data.PassportNo)))
            throw new UnauthorizedAccessException("Identity fields require hr.employee.view_identity.");
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static void Require(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"{label} is required and must not exceed {maxLength} characters.");
    }

    private static void Limit(string? value, int maxLength, string label)
    {
        if (value?.Trim().Length > maxLength)
            throw new ArgumentException($"{label} must not exceed {maxLength} characters.");
    }
}

public sealed class UpdateHrEmployeeCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<UpdateHrEmployeeCommand, HrEmployeeProfile?>
{
    public async Task<HrEmployeeProfile?> Handle(
        UpdateHrEmployeeCommand request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0 || request.ExpectedVersion <= 0)
            throw new ArgumentException("Employee identity and expected version must be positive.");
        CreateHrEmployeeCommandHandler.ValidatePerson(
            null, request.Data.FirstName, request.Data.MiddleName, request.Data.LastName,
            request.Data.DisplayName, request.Data.GenderCode, request.Data.NationalId,
            request.Data.PassportNo, request.Data.PersonalEmail, request.Data.WorkEmail,
            request.Data.Mobile, request.Data.PresentAddress, request.Data.PermanentAddress,
            request.Data.EmergencyContactName, request.Data.EmergencyContactPhone);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.Edit, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var fieldAccess = access.GetFieldAccess();
        CreateHrEmployeeCommandHandler.EnsureSensitiveWriteAccess(request.Data, fieldAccess);
        access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "update", "hr", "Update employee",
                "HrEmployee", request.EmployeeId.ToString()),
            token => store.UpdateAsync(businessId, request.EmployeeId, request.ExpectedVersion,
                request.Data, fieldAccess, token),
            result => result is not null,
            cancellationToken);
    }
}
