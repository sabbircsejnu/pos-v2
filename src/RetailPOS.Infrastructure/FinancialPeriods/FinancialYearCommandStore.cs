using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Application.FinancialPeriods.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.FinancialPeriods;

public sealed class FinancialYearCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    ILogger<FinancialYearCommandStore> logger)
    : IFinancialYearCommandStore
{
    public async Task<FinancialYearReadModel> CreateAsync(
        long businessId,
        CreateFinancialYearCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDateRange(command.StartDate, command.EndDate);
            var startDate = command.StartDate.Date;
            var endDate = command.EndDate.Date;
            await EnsureYearDoesNotOverlapAsync(
                businessId,
                startDate,
                endDate,
                excludeYearId: null,
                cancellationToken);

            var now = DateTime.UtcNow;
            var year = new FinancialYear
            {
                BusinessId = businessId,
                YearCode = command.YearCode.Trim(),
                Name = command.Name.Trim(),
                StartDate = startDate,
                EndDate = endDate,
                IsCurrent = command.IsCurrent,
                IsClosed = false,
                CreatedAt = now,
                CreatedBy = command.ActorUserId,
                UpdatedAt = now,
                UpdatedBy = command.ActorUserId
            };

            if (year.IsCurrent)
            {
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await UnsetExistingCurrentYearAsync(
                    businessId,
                    command.ActorUserId,
                    now,
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.FinancialYears.Add(year);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                dbContext.FinancialYears.Add(year);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "Financial year {FinancialYearId} created for business {BusinessId}",
                year.Id,
                businessId);
            await RecordYearAuditAsync(
                AuditActionType.Create,
                $"Financial year created: {year.Id}",
                year,
                cancellationToken);
            return Map(year);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Create,
                "Financial year creation rejected",
                entityId: null,
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task<FinancialYearReadModel> UpdateAsync(
        long businessId,
        UpdateFinancialYearCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDateRange(command.StartDate, command.EndDate);

            var year = await dbContext.FinancialYears
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId && item.Id == command.Id,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Financial year with ID {command.Id} was not found.");

            if (year.IsClosed)
            {
                throw new InvalidOperationException("Closed financial years cannot be updated.");
            }

            var startDate = command.StartDate.Date;
            var endDate = command.EndDate.Date;
            await EnsureYearDoesNotOverlapAsync(
                businessId,
                startDate,
                endDate,
                year.Id,
                cancellationToken);

            year.YearCode = command.YearCode.Trim();
            year.Name = command.Name.Trim();
            year.StartDate = startDate;
            year.EndDate = endDate;
            year.UpdatedAt = DateTime.UtcNow;
            year.UpdatedBy = command.ActorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Financial year {FinancialYearId} updated for business {BusinessId}",
                year.Id,
                businessId);
            await RecordYearAuditAsync(
                AuditActionType.Update,
                $"Financial year updated: {year.Id}",
                year,
                cancellationToken);
            return Map(year);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Update,
                $"Financial year update rejected: {command.Id}",
                command.Id.ToString(),
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task<FinancialYearReadModel> ActivateAsync(
        long businessId,
        ActivateFinancialYearCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var year = await dbContext.FinancialYears
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId && item.Id == command.Id,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Financial year with ID {command.Id} was not found.");

            if (year.IsClosed)
            {
                throw new InvalidOperationException("Closed financial years cannot be activated.");
            }

            var now = DateTime.UtcNow;
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await UnsetExistingCurrentYearAsync(
                businessId,
                command.ActorUserId,
                now,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            year.IsCurrent = true;
            year.UpdatedAt = now;
            year.UpdatedBy = command.ActorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Financial year {FinancialYearId} activated for business {BusinessId}",
                year.Id,
                businessId);
            await RecordYearAuditAsync(
                AuditActionType.Update,
                $"Financial year activated: {year.Id}",
                year,
                cancellationToken);
            return Map(year);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Update,
                $"Financial year activate rejected: {command.Id}",
                command.Id.ToString(),
                exception,
                cancellationToken);
            throw;
        }
    }

    private async Task EnsureYearDoesNotOverlapAsync(
        long businessId,
        DateTime startDate,
        DateTime endDate,
        long? excludeYearId,
        CancellationToken cancellationToken)
    {
        var overlaps = await dbContext.FinancialYears.AnyAsync(
            year => year.BusinessId == businessId
                && (!excludeYearId.HasValue || year.Id != excludeYearId.Value)
                && startDate <= year.EndDate.Date
                && endDate >= year.StartDate.Date,
            cancellationToken);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "Financial year date range overlaps with an existing financial year.");
        }
    }

    private async Task UnsetExistingCurrentYearAsync(
        long businessId,
        long actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var currentYears = await dbContext.FinancialYears
            .Where(year => year.BusinessId == businessId && year.IsCurrent)
            .ToListAsync(cancellationToken);

        foreach (var year in currentYears)
        {
            year.IsCurrent = false;
            year.UpdatedAt = now;
            year.UpdatedBy = actorUserId;
        }
    }

    private async Task RecordYearAuditAsync(
        string actionType,
        string summary,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = new Dictionary<string, object?>
            {
                ["financialYearId"] = year.Id,
                ["businessId"] = year.BusinessId,
                ["yearCode"] = year.YearCode,
                ["isClosed"] = year.IsClosed,
                ["isCurrent"] = year.IsCurrent
            };
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = actionType,
                Module = AuditModule.Accounts,
                Summary = summary,
                PrimaryEntity = ("FinancialYear", year.Id.ToString()),
                Metadata = metadata
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record financial year audit event for year {FinancialYearId}",
                year.Id);
        }
    }

    private async Task RecordFailureAuditAsync(
        string actionType,
        string summary,
        string? entityId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordFailureAsync(
                actionType,
                AuditModule.Accounts,
                summary,
                "FinancialYear",
                entityId,
                exception.Message,
                cancellationToken);
        }
        catch (Exception auditException)
        {
            logger.LogWarning(
                auditException,
                "Failed to record rejected financial year audit event");
        }
    }

    private static void ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        if (startDate.Date > endDate.Date)
        {
            throw new InvalidOperationException(
                "Invalid financial year range. Start date must be less than or equal to end date.");
        }
    }

    private static FinancialYearReadModel Map(FinancialYear year) =>
        new(
            year.Id,
            year.YearCode,
            year.Name,
            year.StartDate,
            year.EndDate,
            year.IsCurrent,
            year.IsClosed,
            year.ClosedAt,
            year.ClosedBy);
}
