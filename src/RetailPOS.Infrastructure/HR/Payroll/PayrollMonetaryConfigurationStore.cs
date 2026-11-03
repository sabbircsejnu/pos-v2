using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollMonetaryConfigurationStore(RetailPOSDbContext db)
    : IPayrollMonetaryConfigurationStore
{
    public async Task<PayrollMonetaryConfigurationResponse> CreateAsync(
        long businessId,
        long actorUserId,
        CreatePayrollMonetaryConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);

        if (await db.PayrollMonetaryConfigurationVersions.AsNoTracking().AnyAsync(row =>
                row.BusinessId == businessId && row.EffectiveFrom == request.EffectiveFrom,
                cancellationToken))
            throw new PayrollMonetaryConfigurationConflictException(
                "A monetary configuration already exists for this effective date.");

        var versionNo = checked((await db.PayrollMonetaryConfigurationVersions
            .Where(row => row.BusinessId == businessId)
            .Select(row => (int?)row.VersionNo).MaxAsync(cancellationToken) ?? 0) + 1);
        var entity = new PayrollMonetaryConfigurationVersion
        {
            BusinessId = businessId,
            VersionNo = versionNo,
            EffectiveFrom = request.EffectiveFrom,
            CurrencyCode = request.CurrencyCode,
            DecimalPrecision = request.DecimalPrecision,
            RoundingMode = request.RoundingMode,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorUserId
        };
        db.PayrollMonetaryConfigurationVersions.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollMonetaryConfigurationConflictException(
                "A concurrent monetary configuration conflicts with this effective date.", exception);
        }
        return Map(entity);
    }

    public async Task<IReadOnlyList<PayrollMonetaryConfigurationResponse>> ListAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollMonetaryConfigurationVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    private static PayrollMonetaryConfigurationResponse Map(
        PayrollMonetaryConfigurationVersion row) =>
        new(row.Id, row.BusinessId, row.VersionNo, row.EffectiveFrom, row.CurrencyCode,
            row.DecimalPrecision, row.RoundingMode, row.CreatedAtUtc, row.CreatedByUserId);

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll monetary configuration writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 8)})",
                cancellationToken);
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll monetary configuration writes require the audited transaction boundary.");
    }
}
