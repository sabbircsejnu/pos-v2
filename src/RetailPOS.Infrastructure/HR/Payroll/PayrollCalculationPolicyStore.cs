using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollCalculationPolicyStore(RetailPOSDbContext db) : IPayrollCalculationPolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PayrollCalculationPolicyResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollCalculationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        request = request with
        {
            PartialMonthBasis = string.IsNullOrWhiteSpace(request.PartialMonthBasis)
                ? "Fixed30"
                : request.PartialMonthBasis.Trim()
        };
        if (request.PartialMonthBasis is not ("CalendarDays" or "Fixed30" or "WorkingDays"))
            throw new ArgumentException("Policy partial-month basis is not supported.");
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll calculation policies require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 18)})", cancellationToken);

        var proposedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
        var overlap = await db.PayrollCalculationPolicyVersions.AsNoTracking().AnyAsync(row =>
                row.BusinessId == businessId
                && row.EffectiveFrom <= proposedEnd
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= request.EffectiveFrom)
                && !(row.EffectiveFrom < request.EffectiveFrom && !row.EffectiveTo.HasValue),
            cancellationToken);
        if (overlap)
            throw new PayrollCalculationPolicyConflictException(
                "The effective period overlaps an existing immutable policy version.");

        var components = new Dictionary<long, PayrollSalaryComponent>();
        var allIds = request.SalaryBaseComponentIds.Concat(new[]
            {
                request.AbsenceComponentId, request.UnpaidLeaveComponentId, request.LateComponentId,
                request.EarlyLeaveComponentId, request.OvertimeComponentId, request.TaxStatutoryComponentId
            }.Where(id => id.HasValue).Select(id => id!.Value))
            .Distinct().ToList();
        if (allIds.Count > 0)
            components = await db.PayrollSalaryComponents.AsNoTracking()
                .Where(row => row.BusinessId == businessId && allIds.Contains(row.Id) && row.IsActive)
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        if (components.Count != allIds.Count)
            throw new ArgumentException("A policy component is missing, inactive, or belongs to another Business.");
        if (request.SalaryBaseComponentIds.Any(id => components[id].ComponentType != "Earning"))
            throw new ArgumentException("Salary-base components must be active earnings in this Business.");
        ValidateComponent(request.AbsenceComponentId, "Deduction");
        ValidateComponent(request.UnpaidLeaveComponentId, "Deduction");
        ValidateComponent(request.LateComponentId, "Deduction");
        ValidateComponent(request.EarlyLeaveComponentId, "Deduction");
        ValidateComponent(request.OvertimeComponentId, "Earning");
        ValidateComponent(request.TaxStatutoryComponentId, "Deduction");

        var versionNo = checked((await db.PayrollCalculationPolicyVersions
            .Where(row => row.BusinessId == businessId)
            .Select(row => (int?)row.VersionNo).MaxAsync(cancellationToken) ?? 0) + 1);
        var baseSnapshots = request.SalaryBaseComponentIds.Order()
            .Select(id => Snapshot(components[id])).ToArray();
        var outputIds = new[]
            {
                request.AbsenceComponentId, request.UnpaidLeaveComponentId, request.LateComponentId,
                request.EarlyLeaveComponentId, request.OvertimeComponentId, request.TaxStatutoryComponentId
            }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order().ToArray();
        var outputSnapshots = outputIds
            .Order().Select(id => Snapshot(components[id])).ToArray();
        var snapshot = JsonSerializer.Serialize(new
        {
            Configuration = request,
            SalaryBaseComponents = baseSnapshots,
            OutputComponents = outputSnapshots
        }, JsonOptions);
        var entity = new PayrollCalculationPolicyVersion
        {
            BusinessId = businessId,
            VersionNo = versionNo,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            PartialMonthBasis = request.PartialMonthBasis,
            SalaryBaseComponentIdsJson = JsonSerializer.Serialize(
                request.SalaryBaseComponentIds.Order().ToArray(), JsonOptions),
            DeductAbsence = request.DeductAbsence,
            DeductUnpaidLeave = request.DeductUnpaidLeave,
            LatePerMinuteRate = request.LatePerMinuteRate,
            EarlyLeavePerMinuteRate = request.EarlyLeavePerMinuteRate,
            OvertimeMode = request.OvertimeMode,
            OvertimeDivisorDays = request.OvertimeDivisorDays,
            StandardPaidMinutes = request.StandardPaidMinutes,
            NormalDayOvertimeRate = request.NormalDayOvertimeRate,
            WeeklyOffOvertimeRate = request.WeeklyOffOvertimeRate,
            HolidayOvertimeRate = request.HolidayOvertimeRate,
            SpecialDayOvertimeRate = request.SpecialDayOvertimeRate,
            AbsenceComponentId = request.AbsenceComponentId,
            UnpaidLeaveComponentId = request.UnpaidLeaveComponentId,
            LateComponentId = request.LateComponentId,
            EarlyLeaveComponentId = request.EarlyLeaveComponentId,
            OvertimeComponentId = request.OvertimeComponentId,
            TaxStatutoryComponentId = request.TaxStatutoryComponentId,
            ConfigurationSnapshotJson = snapshot,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorUserId
        };
        db.PayrollCalculationPolicyVersions.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollCalculationPolicyConflictException(
                "A concurrent policy version conflicts with this effective period.", exception);
        }
        return Map(entity);

        void ValidateComponent(long? id, string requiredType)
        {
            if (id.HasValue && components[id.Value].ComponentType != requiredType)
                throw new ArgumentException(
                    $"Policy output component {id.Value} must be an active {requiredType} component in this Business.");
        }
    }

    public async Task<IReadOnlyList<PayrollCalculationPolicyResponse>> ListAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollCalculationPolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    private static PayrollCalculationPolicyResponse Map(PayrollCalculationPolicyVersion row)
    {
        var baseIds = JsonSerializer.Deserialize<long[]>(row.SalaryBaseComponentIdsJson, JsonOptions)
            ?? throw new InvalidOperationException("Payroll policy salary-base snapshot is invalid.");
        return new PayrollCalculationPolicyResponse(
            row.Id, row.BusinessId, row.VersionNo, row.EffectiveFrom, row.EffectiveTo,
            row.PartialMonthBasis, baseIds, row.DeductAbsence, row.DeductUnpaidLeave,
            row.LatePerMinuteRate, row.EarlyLeavePerMinuteRate, row.OvertimeMode,
            row.OvertimeDivisorDays, row.StandardPaidMinutes, row.NormalDayOvertimeRate,
            row.WeeklyOffOvertimeRate, row.HolidayOvertimeRate, row.SpecialDayOvertimeRate,
            row.AbsenceComponentId, row.UnpaidLeaveComponentId, row.LateComponentId,
            row.EarlyLeaveComponentId, row.OvertimeComponentId, row.TaxStatutoryComponentId,
            row.ConfigurationSnapshotJson, row.CreatedAtUtc, row.CreatedByUserId);
    }

    private static ComponentSnapshot Snapshot(PayrollSalaryComponent component) =>
        new(component.Id, component.Code, component.Name, component.ComponentType);

    private sealed record ComponentSnapshot(long Id, string Code, string Name, string ComponentType);
}

public sealed class PayrollCalculationPolicyConflictException(
    string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
