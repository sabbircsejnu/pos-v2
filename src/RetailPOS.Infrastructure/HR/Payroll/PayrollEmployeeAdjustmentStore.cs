using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollEmployeeAdjustmentStore(
    RetailPOSDbContext db,
    IPayrollSalarySetupStore salaries,
    TimeProvider? clock = null) : IPayrollEmployeeAdjustmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> EffectiveAsync(
        long businessId, long employeeId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PayrollEmployeeAdjustmentVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.EffectiveFrom <= asOf)
            .OrderBy(row => row.AdjustmentIdentity)
            .ThenByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.Version)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.AdjustmentIdentity)
            .Select(group => group.First())
            .Where(row => row.Status == "Active" && (row.EffectiveTo is null || row.EffectiveTo >= asOf))
            .OrderBy(row => row.AdjustmentKind, StringComparer.Ordinal)
            .ThenBy(row => row.ComponentCodeSnapshot, StringComparer.Ordinal)
            .ThenBy(row => row.AdjustmentIdentity)
            .Select(Map)
            .ToArray();
    }

    public async Task<IReadOnlyList<PayrollEmployeeAdjustmentResponse>> HistoryAsync(
        long businessId, Guid adjustmentIdentity, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PayrollEmployeeAdjustmentVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.AdjustmentIdentity == adjustmentIdentity)
            .OrderBy(row => row.Version)
            .ToArrayAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public async Task<PayrollEmployeeAdjustmentResponse> SaveAsync(
        long businessId, long actorUserId, SavePayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await LockEmployeeAsync(businessId, request.EmployeeId, cancellationToken);
        var key = request.IdempotencyKey.Trim();
        var hash = Hash(Normalize(request));
        var prior = await db.Set<PayrollEmployeeAdjustmentVersion>().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.EmployeeId == request.EmployeeId && row.IdempotencyKey == key, cancellationToken);
        if (prior is not null)
        {
            EnsureSamePayload(prior, hash);
            return Map(prior) with { IsReplay = true };
        }

        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == request.EmployeeId
                && period.StartDate <= request.EffectiveFrom
                && (period.EndDate == null || period.EndDate >= request.EffectiveFrom), cancellationToken))
            throw new ArgumentException("The employee is not active and employed on the effective date.");

        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, request.EmployeeId, request.EffectiveFrom, cancellationToken);
        var component = await db.PayrollSalaryComponents.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.SalaryComponentId && row.IsActive,
                cancellationToken)
            ?? throw new KeyNotFoundException("Active salary component was not found in this Business.");
        ValidateComponent(request, component.ComponentType, component.ValueType, component.IsRecurring);

        PayrollEmployeeAdjustmentVersion? predecessor = null;
        var identity = request.AdjustmentIdentity ?? Guid.NewGuid();
        var version = 1;
        if (request.AdjustmentIdentity.HasValue)
        {
            predecessor = await db.Set<PayrollEmployeeAdjustmentVersion>()
                .Where(row => row.BusinessId == businessId && row.EmployeeId == request.EmployeeId
                    && row.AdjustmentIdentity == identity)
                .OrderByDescending(row => row.Version)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Payroll adjustment was not found in this Business.");
            if (request.ExpectedVersion != predecessor.Version)
                throw new PayrollSalaryConflictException("Payroll adjustment changed since it was read.");
            if (!IsRecurring(predecessor.AdjustmentKind)
                || request.AdjustmentKind != predecessor.AdjustmentKind
                || request.SalaryComponentId != predecessor.SalaryComponentId)
                throw new ArgumentException(
                    "Only recurring adjustments may be versioned; their type and component are immutable.");
            if (predecessor.Status != "Active" || request.EffectiveFrom <= predecessor.EffectiveFrom)
                throw new PayrollSalaryConflictException(
                    "A recurring adjustment successor must be effective after its active predecessor.");
            version = checked(predecessor.Version + 1);
        }

        var row = new PayrollEmployeeAdjustmentVersion
        {
            BusinessId = businessId,
            EmployeeId = request.EmployeeId,
            AdjustmentIdentity = identity,
            Version = version,
            PredecessorVersionId = predecessor?.Id,
            SalaryComponentId = component.Id,
            ComponentCodeSnapshot = component.Code,
            ComponentNameSnapshot = component.Name,
            ComponentTypeSnapshot = component.ComponentType,
            ValueTypeSnapshot = component.ValueType,
            AdjustmentKind = request.AdjustmentKind,
            Classification = request.Classification,
            Amount = request.Amount,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            OperationalLocationProjectionId = locationId,
            Reason = request.Reason.Trim(),
            Reference = NormalizeOptional(request.Reference),
            Status = "Active",
            IdempotencyKey = key,
            IdempotencyPayloadHash = hash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc()
        };
        db.Add(row);
        await SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollEmployeeAdjustmentResponse> EndAsync(
        long businessId, long actorUserId, EndPayrollEmployeeAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AdjustmentIdentity == Guid.Empty || request.ExpectedVersion <= 0
            || request.EffectiveFrom == default || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 1000 || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid adjustment identity, version, effective date, reason and key are required.");

        var head = await db.Set<PayrollEmployeeAdjustmentVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.AdjustmentIdentity == request.AdjustmentIdentity)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Payroll adjustment was not found in this Business.");
        await LockEmployeeAsync(businessId, head.EmployeeId, cancellationToken);
        var hash = Hash(new
        {
            request.AdjustmentIdentity,
            request.ExpectedVersion,
            request.EffectiveFrom,
            Reason = request.Reason.Trim()
        });
        var prior = await db.Set<PayrollEmployeeAdjustmentVersion>().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.EmployeeId == head.EmployeeId && row.IdempotencyKey == request.IdempotencyKey.Trim(),
                cancellationToken);
        if (prior is not null)
        {
            EnsureSamePayload(prior, hash);
            return Map(prior) with { IsReplay = true };
        }

        var current = await db.Set<PayrollEmployeeAdjustmentVersion>().SingleAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == head.EmployeeId
            && row.AdjustmentIdentity == request.AdjustmentIdentity
            && row.Version == request.ExpectedVersion, cancellationToken);
        if (!IsRecurring(current.AdjustmentKind))
            throw new ArgumentException("One-time adjustments cannot be ended or edited.");
        if (current.Status != "Active" || request.EffectiveFrom <= current.EffectiveFrom)
            throw new PayrollSalaryConflictException(
                "An adjustment can only be ended after its active version's effective date.");
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, current.EmployeeId, request.EffectiveFrom, cancellationToken);
        var ended = new PayrollEmployeeAdjustmentVersion
        {
            BusinessId = current.BusinessId,
            EmployeeId = current.EmployeeId,
            AdjustmentIdentity = current.AdjustmentIdentity,
            Version = checked(current.Version + 1),
            PredecessorVersionId = current.Id,
            SalaryComponentId = current.SalaryComponentId,
            ComponentCodeSnapshot = current.ComponentCodeSnapshot,
            ComponentNameSnapshot = current.ComponentNameSnapshot,
            ComponentTypeSnapshot = current.ComponentTypeSnapshot,
            ValueTypeSnapshot = current.ValueTypeSnapshot,
            AdjustmentKind = current.AdjustmentKind,
            Classification = current.Classification,
            Amount = current.Amount,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null,
            OperationalLocationProjectionId = locationId,
            Reason = request.Reason.Trim(),
            Reference = current.Reference,
            Status = "Inactive",
            IdempotencyKey = request.IdempotencyKey.Trim(),
            IdempotencyPayloadHash = hash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc()
        };
        db.Add(ended);
        await SaveChangesAsync(cancellationToken);
        return Map(ended);
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll adjustment writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
        {
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {businessId} AND id = {employeeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                throw new KeyNotFoundException("Employee was not found in this Business.");
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException(
                "Payroll adjustment serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollSalaryConflictException("Payroll adjustment changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollSalaryConflictException(
                "A payroll adjustment version or idempotency key already exists.", exception);
        }
    }

    private DateTime NowUtc() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static void Validate(SavePayrollEmployeeAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId <= 0 || request.SalaryComponentId <= 0
            || request.EffectiveFrom == default || request.EffectiveTo < request.EffectiveFrom
            || request.Amount <= 0 || request.Amount > 99999999999999.9999m
            || decimal.Round(request.Amount, 4) != request.Amount
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || request.Reference?.Length > 150
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("Payroll adjustment data contains an invalid amount, date or required field.");
        if (request.AdjustmentIdentity.HasValue != request.ExpectedVersion.HasValue
            || request.AdjustmentIdentity == Guid.Empty || request.ExpectedVersion is <= 0)
            throw new ArgumentException("Adjustment updates require an identity and expected version.");
        if (request.AdjustmentKind is not (
                "RecurringAllowance" or "RecurringDeduction" or "OneTimeEarning" or "OneTimeDeduction"))
            throw new ArgumentException("The adjustment kind is not supported.");
        if (request.Classification is not ("Manual" or "Statutory" or "Incentive" or "Bonus"))
            throw new ArgumentException("The adjustment classification is not supported.");
        if (request.Classification is "Incentive" or "Bonus"
            && request.AdjustmentKind is not ("OneTimeEarning"))
            throw new ArgumentException("Incentive and bonus adjustments must be one-time earnings.");
        if (request.Classification == "Statutory"
            && request.AdjustmentKind is not ("RecurringDeduction" or "OneTimeDeduction"))
            throw new ArgumentException("Statutory adjustments must be deductions.");
    }

    private static void ValidateComponent(
        SavePayrollEmployeeAdjustmentRequest request,
        string componentType,
        string valueType,
        bool isRecurring)
    {
        var deduction = request.AdjustmentKind is "RecurringDeduction" or "OneTimeDeduction";
        var recurring = IsRecurring(request.AdjustmentKind);
        if (componentType != (deduction ? "Deduction" : "Earning"))
            throw new ArgumentException("The selected component type does not match the adjustment kind.");
        if (isRecurring != recurring)
            throw new ArgumentException("The selected component recurring setting does not match the adjustment kind.");
        if (valueType != "Fixed")
            throw new ArgumentException(
                "Phase 4 adjustment evidence accepts fixed amounts only; percentage/formula evaluation belongs to Payroll calculation.");
    }

    private static bool IsRecurring(string kind) =>
        kind is "RecurringAllowance" or "RecurringDeduction";

    private static object Normalize(SavePayrollEmployeeAdjustmentRequest request) => new
    {
        request.AdjustmentIdentity,
        request.ExpectedVersion,
        request.EmployeeId,
        request.SalaryComponentId,
        request.AdjustmentKind,
        request.Classification,
        request.Amount,
        request.EffectiveFrom,
        request.EffectiveTo,
        Reason = request.Reason.Trim(),
        Reference = NormalizeOptional(request.Reference)
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, JsonOptions))));

    private static void EnsureSamePayload(PayrollEmployeeAdjustmentVersion row, string hash)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(row.IdempotencyPayloadHash),
                Convert.FromHexString(hash)))
            throw new PayrollSalaryConflictException(
                "The idempotency key was already used with a different adjustment payload.");
    }

    private static PayrollEmployeeAdjustmentResponse Map(PayrollEmployeeAdjustmentVersion row) => new(
        row.Id, row.BusinessId, row.EmployeeId, row.AdjustmentIdentity, row.Version,
        row.PredecessorVersionId, row.SalaryComponentId, row.ComponentCodeSnapshot,
        row.ComponentNameSnapshot, row.ComponentTypeSnapshot, row.ValueTypeSnapshot,
        row.AdjustmentKind, row.Classification, row.Amount, row.EffectiveFrom,
        row.EffectiveTo, row.OperationalLocationProjectionId, row.Reason, row.Reference,
        row.Status, row.CreatedByUserId, row.CreatedAtUtc);
}
