using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.FinancialPeriods;

public sealed class FinancialPeriodReadStore(RetailPOSDbContext dbContext)
    : IFinancialPeriodReadStore
{
    public async Task<IReadOnlyList<FinancialYearReadModel>> GetFinancialYearsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        return await dbContext.FinancialYears
            .AsNoTracking()
            .Where(year => year.BusinessId == businessId)
            .OrderByDescending(year => year.StartDate)
            .ThenByDescending(year => year.Id)
            .Select(year => new FinancialYearReadModel(
                year.Id,
                year.YearCode,
                year.Name,
                year.StartDate,
                year.EndDate,
                year.IsCurrent,
                year.IsClosed,
                year.ClosedAt,
                year.ClosedBy))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> FinancialYearExistsAsync(
        long businessId,
        long financialYearId,
        CancellationToken cancellationToken)
    {
        return dbContext.FinancialYears.AnyAsync(
            year => year.BusinessId == businessId && year.Id == financialYearId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingPeriodReadModel>> GetPeriodsAsync(
        long businessId,
        long financialYearId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(period =>
                period.FinancialYearId == financialYearId &&
                period.FinancialYear.BusinessId == businessId)
            .OrderBy(period => period.StartDate)
            .ThenBy(period => period.PeriodNumber)
            .Select(period => new AccountingPeriodReadModel(
                period.Id,
                period.FinancialYearId,
                period.PeriodNumber,
                period.Name,
                period.StartDate,
                period.EndDate,
                period.Status,
                period.StatusReason,
                period.ClosedAt,
                period.ClosedBy,
                period.LockedAt,
                period.LockedBy,
                period.ReopenedAt,
                period.ReopenedBy))
            .ToListAsync(cancellationToken);
    }
}
