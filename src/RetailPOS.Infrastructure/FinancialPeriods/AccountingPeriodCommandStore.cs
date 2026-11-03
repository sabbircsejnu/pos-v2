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

public sealed class AccountingPeriodCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    ILogger<AccountingPeriodCommandStore> logger)
    : IAccountingPeriodCommandStore
{
    public async Task<AccountingPeriodReadModel> CreateAsync(
        long businessId,
        CreateAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDateRange(command.StartDate, command.EndDate);
            ValidatePeriodNumber(command.PeriodNumber);

            var year = await dbContext.FinancialYears
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId && item.Id == command.FinancialYearId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Financial year with ID {command.FinancialYearId} was not found.");

            var startDate = command.StartDate.Date;
            var endDate = command.EndDate.Date;
            ValidatePeriodInsideYear(year, startDate, endDate);
            ValidatePeriodStatus(command.Status);
            await EnsurePeriodDoesNotOverlapAsync(
                command.FinancialYearId,
                startDate,
                endDate,
                excludePeriodId: null,
                cancellationToken);

            var now = DateTime.UtcNow;
            var period = new AccountingPeriod
            {
                FinancialYearId = command.FinancialYearId,
                PeriodNumber = command.PeriodNumber,
                Name = command.Name.Trim(),
                StartDate = startDate,
                EndDate = endDate,
                Status = NormalizeStatus(command.Status),
                CreatedAt = now,
                CreatedBy = command.ActorUserId,
                UpdatedAt = now,
                UpdatedBy = command.ActorUserId
            };

            if (period.Status == AccountingPeriodStatuses.Closed)
            {
                period.ClosedAt = now;
                period.ClosedBy = command.ActorUserId;
            }

            if (period.Status == AccountingPeriodStatuses.Locked)
            {
                period.LockedAt = now;
                period.LockedBy = command.ActorUserId;
            }

            dbContext.AccountingPeriods.Add(period);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Accounting period {PeriodId} created in financial year {FinancialYearId}",
                period.Id,
                command.FinancialYearId);
            await RecordPeriodAuditAsync(
                AuditActionType.Create,
                $"Accounting period created: {period.Id}",
                period,
                cancellationToken);
            return Map(period);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Create,
                $"Accounting period creation rejected for year: {command.FinancialYearId}",
                entityId: null,
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task<AccountingPeriodReadModel> UpdateAsync(
        long businessId,
        UpdateAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDateRange(command.StartDate, command.EndDate);
            ValidatePeriodNumber(command.PeriodNumber);

            var period = await dbContext.AccountingPeriods
                .Include(item => item.FinancialYear)
                .FirstOrDefaultAsync(
                    item => item.Id == command.Id
                        && item.FinancialYear.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Accounting period with ID {command.Id} was not found.");

            if (period.Status == AccountingPeriodStatuses.Locked)
            {
                throw new InvalidOperationException("Locked accounting periods cannot be updated.");
            }

            var startDate = command.StartDate.Date;
            var endDate = command.EndDate.Date;
            ValidatePeriodInsideYear(period.FinancialYear, startDate, endDate);
            await EnsurePeriodDoesNotOverlapAsync(
                period.FinancialYearId,
                startDate,
                endDate,
                period.Id,
                cancellationToken);

            period.PeriodNumber = command.PeriodNumber;
            period.Name = command.Name.Trim();
            period.StartDate = startDate;
            period.EndDate = endDate;
            period.UpdatedAt = DateTime.UtcNow;
            period.UpdatedBy = command.ActorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Accounting period {PeriodId} updated", period.Id);
            await RecordPeriodAuditAsync(
                AuditActionType.Update,
                $"Accounting period updated: {period.Id}",
                period,
                cancellationToken);
            return Map(period);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Update,
                $"Accounting period update rejected: {command.Id}",
                command.Id.ToString(),
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task<AccountingPeriodReadModel> UpdateStatusAsync(
        long businessId,
        UpdateAccountingPeriodStatusCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidatePeriodStatus(command.Status);

            var period = await dbContext.AccountingPeriods
                .Include(item => item.FinancialYear)
                .FirstOrDefaultAsync(
                    item => item.Id == command.Id
                        && item.FinancialYear.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Accounting period with ID {command.Id} was not found.");

            var normalizedStatus = NormalizeStatus(command.Status);
            if (period.Status == normalizedStatus)
            {
                await RecordPeriodAuditAsync(
                    AuditActionType.Update,
                    $"Accounting period status update requested with no change: {period.Id}",
                    period,
                    cancellationToken,
                    new Dictionary<string, object?>
                    {
                        ["noOp"] = true,
                        ["requestedStatus"] = normalizedStatus,
                        ["reason"] = string.IsNullOrWhiteSpace(command.Reason)
                            ? null
                            : command.Reason.Trim()
                    });
                return Map(period);
            }

            ValidateStatusTransition(period.Status, normalizedStatus);

            var normalizedReason = string.IsNullOrWhiteSpace(command.Reason)
                ? null
                : command.Reason.Trim();
            if (normalizedStatus == AccountingPeriodStatuses.Open
                && string.IsNullOrWhiteSpace(normalizedReason))
            {
                throw new InvalidOperationException(
                    "Reopen reason is required when changing status to Open.");
            }

            if (normalizedStatus == AccountingPeriodStatuses.Locked
                && string.IsNullOrWhiteSpace(normalizedReason))
            {
                throw new InvalidOperationException(
                    "Lock reason is required when changing status to Locked.");
            }

            var now = DateTime.UtcNow;
            if (normalizedStatus == AccountingPeriodStatuses.Open)
            {
                period.ReopenedAt = now;
                period.ReopenedBy = command.ActorUserId;
            }
            else if (normalizedStatus == AccountingPeriodStatuses.Closed)
            {
                period.ClosedAt = now;
                period.ClosedBy = command.ActorUserId;
            }
            else if (normalizedStatus == AccountingPeriodStatuses.Locked)
            {
                period.LockedAt = now;
                period.LockedBy = command.ActorUserId;
            }

            period.Status = normalizedStatus;
            period.StatusReason = normalizedReason;
            period.UpdatedAt = now;
            period.UpdatedBy = command.ActorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Accounting period {PeriodId} status changed to {Status} by user {UserId}. Reason: {Reason}",
                period.Id,
                period.Status,
                command.ActorUserId,
                period.StatusReason ?? "not provided");
            await RecordPeriodAuditAsync(
                AuditActionType.Update,
                $"Accounting period status changed: {period.Id}",
                period,
                cancellationToken,
                new Dictionary<string, object?>
                {
                    ["status"] = period.Status,
                    ["reason"] = period.StatusReason
                });
            return Map(period);
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                AuditActionType.Update,
                $"Accounting period status update rejected: {command.Id}",
                command.Id.ToString(),
                exception,
                cancellationToken);
            throw;
        }
    }

    private async Task EnsurePeriodDoesNotOverlapAsync(
        long financialYearId,
        DateTime startDate,
        DateTime endDate,
        long? excludePeriodId,
        CancellationToken cancellationToken)
    {
        var overlaps = await dbContext.AccountingPeriods.AnyAsync(
            period => period.FinancialYearId == financialYearId
                && (!excludePeriodId.HasValue || period.Id != excludePeriodId.Value)
                && startDate <= period.EndDate.Date
                && endDate >= period.StartDate.Date,
            cancellationToken);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "Accounting period date range overlaps with an existing period in the same financial year.");
        }
    }

    private async Task RecordPeriodAuditAsync(
        string actionType,
        string summary,
        AccountingPeriod period,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? additionalMetadata = null)
    {
        try
        {
            var metadata = new Dictionary<string, object?>(additionalMetadata ?? new Dictionary<string, object?>())
            {
                ["accountingPeriodId"] = period.Id,
                ["financialYearId"] = period.FinancialYearId,
                ["periodNumber"] = period.PeriodNumber,
                ["status"] = period.Status
            };
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = actionType,
                Module = AuditModule.Accounts,
                Summary = summary,
                PrimaryEntity = ("AccountingPeriod", period.Id.ToString()),
                Metadata = metadata
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record accounting period audit event for period {AccountingPeriodId}",
                period.Id);
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
                "AccountingPeriod",
                entityId,
                exception.Message,
                cancellationToken);
        }
        catch (Exception auditException)
        {
            logger.LogWarning(
                auditException,
                "Failed to record rejected accounting period audit event");
        }
    }

    private static void ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        if (startDate.Date > endDate.Date)
        {
            throw new InvalidOperationException(
                "Invalid accounting period range. Start date must be less than or equal to end date.");
        }
    }

    private static void ValidatePeriodInsideYear(
        FinancialYear year,
        DateTime startDate,
        DateTime endDate)
    {
        if (startDate < year.StartDate.Date || endDate > year.EndDate.Date)
        {
            throw new InvalidOperationException(
                "Accounting period must be fully contained within its financial year date range.");
        }
    }

    private static void ValidatePeriodStatus(string status)
    {
        if (!string.Equals(status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, AccountingPeriodStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, AccountingPeriodStatuses.Locked, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported accounting period status '{status}'.");
        }
    }

    private static void ValidatePeriodNumber(int periodNumber)
    {
        if (periodNumber <= 0)
        {
            throw new InvalidOperationException(
                "Accounting period number must be greater than zero.");
        }
    }

    private static void ValidateStatusTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(currentStatus, AccountingPeriodStatuses.Locked, StringComparison.OrdinalIgnoreCase)
            && string.Equals(nextStatus, AccountingPeriodStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Locked accounting period must be reopened before it can be closed.");
        }
    }

    private static string NormalizeStatus(string status)
    {
        if (string.Equals(status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPeriodStatuses.Open;
        }

        if (string.Equals(status, AccountingPeriodStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPeriodStatuses.Closed;
        }

        return AccountingPeriodStatuses.Locked;
    }

    private static AccountingPeriodReadModel Map(AccountingPeriod period) =>
        new(
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
            period.ReopenedBy);
}
