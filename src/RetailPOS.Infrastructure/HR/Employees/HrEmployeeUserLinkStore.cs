using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities.HR.Employees;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed partial class HrEmployeeStore
{
    public async Task<HrEmployeeUserLinkReadModel?> GetActiveUserLinkAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var link = await db.HrEmployeeUserLinks.AsNoTracking()
            .Where(link => link.BusinessId == businessId && link.EmployeeId == employeeId && link.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        return link is null ? null : ToUserLinkReadModel(link);
    }

    public async Task<HrEmployeeUserLinkReadModel?> LinkUserAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long identityUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!await db.HrEmployees.AnyAsync(
                employee => employee.BusinessId == businessId && employee.Id == employeeId,
                cancellationToken))
            return null;
        if (await db.HrEmployeeUserLinks.AnyAsync(link =>
                link.IsActive && link.BusinessId == businessId && link.EmployeeId == employeeId,
                cancellationToken))
            throw new InvalidOperationException("The employee already has an active Login User link.");
        if (await db.HrEmployeeUserLinks.AnyAsync(link =>
                link.IsActive && link.IdentityUserId == identityUserId,
                cancellationToken))
            throw new InvalidOperationException("The Login User already has an active employee link.");

        var link = new HrEmployeeUserLink
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            IdentityUserId = identityUserId,
            LinkedAtUtc = DateTime.UtcNow,
            LinkedByUserId = actorUserId,
            LinkReason = reason,
            IsActive = true,
            Version = 1
        };
        db.HrEmployeeUserLinks.Add(link);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveLinkUniqueConflict(exception))
        {
            throw new InvalidOperationException(
                "The employee or Login User already has an active link.", exception);
        }
        return ToUserLinkReadModel(link);
    }

    public async Task<HrEmployeeUserLinkReadModel?> UnlinkUserAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        var link = await db.HrEmployeeUserLinks.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.EmployeeId == employeeId && row.IsActive,
            cancellationToken);
        if (link is null)
            return null;
        if (link.Version != expectedVersion)
            throw new HrEmployeeConcurrencyException("Employee Login User link changed since it was read.");
        link.IsActive = false;
        link.UnlinkedAtUtc = DateTime.UtcNow;
        link.UnlinkedByUserId = actorUserId;
        link.UnlinkReason = reason;
        link.Version = checked(link.Version + 1);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrEmployeeConcurrencyException("Employee Login User link was changed by another request.",
                exception);
        }
        return ToUserLinkReadModel(link);
    }

    public Task<HrEmployeeSelfReadModel?> GetCurrentEmployeeAsync(
        long businessId, long identityUserId, CancellationToken cancellationToken) =>
        (from link in db.HrEmployeeUserLinks.AsNoTracking()
         where link.BusinessId == businessId && link.IdentityUserId == identityUserId && link.IsActive
         join employee in db.HrEmployees.AsNoTracking()
             on new { link.BusinessId, link.EmployeeId }
             equals new { employee.BusinessId, EmployeeId = employee.Id }
         select new HrEmployeeSelfReadModel(
             employee.Id, employee.EmployeeCode, employee.DisplayName, employee.CurrentEmploymentStatus))
        .SingleOrDefaultAsync(cancellationToken);

    private static HrEmployeeUserLinkReadModel ToUserLinkReadModel(HrEmployeeUserLink link) =>
        new(link.Id, link.EmployeeId, link.IdentityUserId,
            new DateTimeOffset(DateTime.SpecifyKind(link.LinkedAtUtc, DateTimeKind.Utc)),
            link.LinkedByUserId, link.LinkReason,
            link.UnlinkedAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(link.UnlinkedAtUtc.Value, DateTimeKind.Utc))
                : null,
            link.UnlinkedByUserId, link.UnlinkReason, link.IsActive, link.Version);

    private static bool IsActiveLinkUniqueConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName is not null
                && (postgres.ConstraintName.Contains("hr_employee_user_links_business_id_employee_id",
                        StringComparison.OrdinalIgnoreCase)
                    || postgres.ConstraintName.Contains("hr_employee_user_links_identity_user_id",
                        StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
