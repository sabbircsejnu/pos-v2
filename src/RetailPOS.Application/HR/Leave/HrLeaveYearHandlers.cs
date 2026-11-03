using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Leave;

namespace RetailPOS.Application.HR.Leave;

public sealed class ReviewHrLeaveYearCloseHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<ReviewHrLeaveYearCloseCommand, HrLeaveYearLifecycleModel>
{
    public async Task<HrLeaveYearLifecycleModel> Handle(
        ReviewHrLeaveYearCloseCommand request, CancellationToken cancellationToken)
    {
        HrLeaveYearCloseValidation.Validate(
            request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
            request.ExpectedVersion, request.IdempotencyKey);
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ReviewLeaveYearClose,
            null, null, request.EmployeeId, cancellationToken);
        return await audit.ExecuteAsync(
            HrLeaveYearCloseValidation.AuditOperation(
                businessId, "hr.leave_year.close_reviewed", request.EmployeeId,
                "Reviewed leave-year closing balances."),
            token => store.ReviewLeaveYearCloseAsync(
                businessId, request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey.Trim(), actorId, token),
            result => !result.IsReplay,
            cancellationToken);
    }
}

public sealed class CloseHrLeaveYearHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CloseHrLeaveYearCommand, HrLeaveYearLifecycleModel>
{
    public async Task<HrLeaveYearLifecycleModel> Handle(
        CloseHrLeaveYearCommand request, CancellationToken cancellationToken)
    {
        HrLeaveYearCloseValidation.Validate(
            request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
            request.ExpectedVersion, request.IdempotencyKey);
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        await access.EnsureAsync(businessId, HrLeaveOperation.CloseLeaveYear,
            null, null, request.EmployeeId, cancellationToken);
        return await audit.ExecuteAsync(
            HrLeaveYearCloseValidation.AuditOperation(
                businessId, "hr.leave_year.closed", request.EmployeeId,
                "Closed an employee leave year."),
            token => store.CloseLeaveYearAsync(
                businessId, request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey.Trim(), actorId, token),
            result => !result.IsReplay,
            cancellationToken);
    }
}

public sealed class ReopenHrLeaveYearHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<ReopenHrLeaveYearCommand, HrLeaveYearLifecycleModel>
{
    public async Task<HrLeaveYearLifecycleModel> Handle(
        ReopenHrLeaveYearCommand request, CancellationToken cancellationToken)
    {
        HrLeaveYearCloseValidation.Validate(
            request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
            request.ExpectedVersion, request.IdempotencyKey);
        CreateHrLeaveTypeHandler.ValidateText(request.Reason, 1000, "Reopen reason");
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ReopenLeaveYear,
            null, null, request.EmployeeId, cancellationToken);
        var reason = request.Reason.Trim();
        return await audit.ExecuteAsync(
            HrLeaveYearCloseValidation.AuditOperation(
                businessId, "hr.leave_year.reopened", request.EmployeeId,
                $"Reopened an employee leave year: {reason}"),
            token => store.ReopenLeaveYearAsync(
                businessId, request.EmployeeId, request.LeaveTypeId, request.LeaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey.Trim(), reason, actorId, token),
            result => !result.IsReplay,
            cancellationToken);
    }
}

internal static class HrLeaveYearCloseValidation
{
    public static void Validate(
        long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string? idempotencyKey)
    {
        if (employeeId <= 0 || leaveTypeId <= 0 || leaveYearStart == default || expectedVersion <= 0)
            throw new ArgumentException("Leave-year identity and expected version are required.");
        CreateHrLeaveTypeHandler.ValidateText(idempotencyKey, 150, "Idempotency key");
    }

    public static RequiredAuditOperation AuditOperation(
        long businessId, string actionType, long employeeId, string summary) =>
        new(businessId, actionType, "HR", summary, "HrEmployee",
            employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
