using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollEmployeePaymentDestinationStore(RetailPOSDbContext db)
    : IPayrollEmployeePaymentDestinationStore
{
    public async Task<PayrollEmployeePaymentDestinationResponse> CreateAsync(
        long businessId, long actorUserId, long employeeId, long locationId,
        PayrollEmployeePaymentDestinationData data, CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        if (await db.PayrollEmployeePaymentDestinations.AsNoTracking().AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId,
                cancellationToken))
            throw new PayrollEmployeePaymentDestinationConflictException(
                "A payment destination already exists; add an effective successor version.");

        var entity = NewEntity(businessId, actorUserId, employeeId, locationId, data,
            versionNo: 1);
        db.PayrollEmployeePaymentDestinations.Add(entity);
        await SaveAsync(cancellationToken);
        return Map(entity, null);
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> VersionAsync(
        long businessId, long actorUserId, long employeeId, long currentDestinationId,
        long expectedVersion, long locationId, PayrollEmployeePaymentDestinationData data,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        var current = await LatestAsync(businessId, employeeId, cancellationToken);
        if (current.Id != currentDestinationId)
            throw new KeyNotFoundException("Payment destination was not found.");
        EnsureCurrentVersion(current, expectedVersion);
        EnsureSuccessorDate(current, data.EffectiveFrom);
        if (!current.IsActive)
            throw new PayrollEmployeePaymentDestinationConflictException(
                "An inactive payment destination cannot be versioned.");

        var entity = NewEntity(businessId, actorUserId, employeeId, locationId, data,
            checked(current.VersionNo + 1));
        db.PayrollEmployeePaymentDestinations.Add(entity);
        await SaveAsync(cancellationToken);
        return Map(entity, null);
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> DeactivateAsync(
        long businessId, long actorUserId, long employeeId, long currentDestinationId,
        long expectedVersion, DateOnly effectiveFrom, long locationId,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        var current = await LatestAsync(businessId, employeeId, cancellationToken);
        if (current.Id != currentDestinationId)
            throw new KeyNotFoundException("Payment destination was not found.");
        EnsureCurrentVersion(current, expectedVersion);
        EnsureSuccessorDate(current, effectiveFrom);
        if (!current.IsActive)
            throw new PayrollEmployeePaymentDestinationConflictException(
                "The payment destination is already inactive.");

        var entity = new PayrollEmployeePaymentDestination
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            VersionNo = checked(current.VersionNo + 1),
            Version = 1,
            OperationalLocationProjectionId = locationId,
            Method = current.Method,
            IsPrimary = true,
            IsActive = false,
            EffectiveFrom = effectiveFrom,
            BankName = current.BankName,
            AccountName = current.AccountName,
            AccountNumber = current.AccountNumber,
            MobileProvider = current.MobileProvider,
            MobileAccountName = current.MobileAccountName,
            MobileAccountNumber = current.MobileAccountNumber,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.PayrollEmployeePaymentDestinations.Add(entity);
        await SaveAsync(cancellationToken);
        return Map(entity, null);
    }

    public async Task<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>> HistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollEmployeePaymentDestinations.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderBy(row => row.EffectiveFrom).ThenBy(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0)
            throw new KeyNotFoundException("Payment destination was not found.");
        return rows.Select((row, index) => Map(row,
            index + 1 < rows.Length ? rows[index + 1].EffectiveFrom.AddDays(-1) : null))
            .ToArray();
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> EffectiveAsync(
        long businessId, long employeeId, DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        var row = await db.PayrollEmployeePaymentDestinations.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.EmployeeId == employeeId
                && item.EffectiveFrom <= effectiveOn)
            .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || !row.IsActive)
            throw new KeyNotFoundException("No active payment destination is effective for this date.");
        return Map(row, null);
    }

    private async Task<PayrollEmployeePaymentDestination> LatestAsync(
        long businessId, long employeeId, CancellationToken cancellationToken) =>
        await db.PayrollEmployeePaymentDestinations
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderByDescending(row => row.VersionNo)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Payment destination was not found.");

    private static PayrollEmployeePaymentDestination NewEntity(
        long businessId, long actorUserId, long employeeId, long locationId,
        PayrollEmployeePaymentDestinationData data, int versionNo) => new()
    {
        BusinessId = businessId,
        EmployeeId = employeeId,
        VersionNo = versionNo,
        Version = 1,
        OperationalLocationProjectionId = locationId,
        Method = data.Method,
        IsPrimary = true,
        IsActive = true,
        EffectiveFrom = data.EffectiveFrom,
        BankName = data.BankName,
        AccountName = data.AccountName,
        AccountNumber = data.AccountNumber,
        MobileProvider = data.MobileProvider,
        MobileAccountName = data.MobileAccountName,
        MobileAccountNumber = data.MobileAccountNumber,
        CreatedByUserId = actorUserId,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static void EnsureCurrentVersion(
        PayrollEmployeePaymentDestination current, long expectedVersion)
    {
        if (current.Version != expectedVersion)
            throw new PayrollEmployeePaymentDestinationConflictException(
                "Payment destination was changed by another request.");
    }

    private static void EnsureSuccessorDate(
        PayrollEmployeePaymentDestination current, DateOnly effectiveFrom)
    {
        if (effectiveFrom <= current.EffectiveFrom || effectiveFrom == DateOnly.MaxValue)
            throw new ArgumentException(
                "A successor payment destination must have a later effective date.");
    }

    private async Task LockEmployeeAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payment destination writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
        {
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {businessId} AND id = {employeeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                throw new KeyNotFoundException("Employee was not found in this Business.");
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException(
                "Payment destination serialization supports PostgreSQL and SQLite tests only.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollEmployeePaymentDestinationConflictException(
                "A concurrent payment destination version conflicts with this request.", exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payment destination writes require the audited transaction boundary.");
    }

    private static PayrollEmployeePaymentDestinationResponse Map(
        PayrollEmployeePaymentDestination row, DateOnly? effectiveTo) =>
        new(row.Id, row.BusinessId, row.EmployeeId, row.VersionNo, row.Version,
            row.OperationalLocationProjectionId, row.Method, row.IsPrimary, row.IsActive,
            row.EffectiveFrom, effectiveTo, row.BankName, row.AccountName, row.AccountNumber,
            row.MobileProvider, row.MobileAccountName, row.MobileAccountNumber,
            row.CreatedAtUtc, row.CreatedByUserId);
}
