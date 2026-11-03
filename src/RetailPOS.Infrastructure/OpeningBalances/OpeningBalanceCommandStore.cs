using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OpeningBalances;

public sealed class OpeningBalanceCommandStore(
    RetailPOSDbContext dbContext,
    IOpeningBalanceMappingValidator mappingValidator,
    IOpeningBalanceJournalPoster journalPoster) : IOpeningBalanceCommandStore
{
    private static readonly HashSet<string> OpeningBalanceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AccountingJournalTypes.OpeningBalance,
        AccountingJournalTypes.OpeningBalanceAdjustment
    };

    private static readonly HashSet<string> AllowedAccountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ChartAccountTypes.Asset,
        ChartAccountTypes.Liability,
        ChartAccountTypes.Equity
    };

    public async Task<long> PostAsync(
        long businessId,
        long actorUserId,
        OpeningBalanceInput input,
        CancellationToken cancellationToken)
    {
        var openingJournal = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.Status == JournalEntryStatuses.Posted
                && journal.JournalType == AccountingJournalTypes.OpeningBalance)
            .OrderBy(journal => journal.PostedAt ?? journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (openingJournal is not null)
        {
            throw new InvalidOperationException(
                "Opening balance is already posted for this business. Use opening balance adjustment for corrections.");
        }

        var lockingJournal = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(journal => journal.BusinessId == businessId
                && journal.Status == JournalEntryStatuses.Posted
                && !OpeningBalanceTypes.Contains(journal.JournalType))
            .OrderBy(journal => journal.PostedAt ?? journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (lockingJournal is not null)
        {
            throw new InvalidOperationException(
                $"Opening balance is locked because operational journal '{lockingJournal.JournalNumber ?? lockingJournal.Id.ToString()}' was posted.");
        }

        var mappingErrors = await mappingValidator.ValidateBalanceSheetMappingsAsync(
            businessId,
            cancellationToken);
        if (mappingErrors.Count > 0)
        {
            throw new InvalidOperationException(BuildMappingValidationMessage(
                mappingErrors,
                "Required mandatory accounting configuration mappings are incomplete or invalid."));
        }

        var lines = await MapAndValidateOpeningLinesAsync(
            businessId,
            input.Lines,
            cancellationToken);
        var startDate = input.AccountingStartDate.Date;
        var journal = new OpeningBalanceJournalInput(
            AccountingJournalTypes.OpeningBalance,
            startDate,
            startDate,
            startDate,
            AccountingReferenceTypes.OpeningBalance,
            businessId.ToString(),
            string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim(),
            string.IsNullOrWhiteSpace(input.Description)
                ? "Opening balance baseline"
                : input.Description.Trim(),
            "opening-balance",
            lines);

        return await journalPoster.CreateOrGetAndPostAsync(
            businessId,
            actorUserId,
            journal,
            cancellationToken);
    }

    public async Task<long> PostAdjustmentAsync(
        long businessId,
        long actorUserId,
        long openingBalanceJournalEntryId,
        OpeningBalanceAdjustmentInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new InvalidOperationException("Adjustment reason is required.");
        }

        var originalOpeningJournal = await dbContext.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                journal => journal.BusinessId == businessId && journal.Id == openingBalanceJournalEntryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Opening balance journal with ID {openingBalanceJournalEntryId} was not found.");

        if (!string.Equals(
                originalOpeningJournal.JournalType,
                AccountingJournalTypes.OpeningBalance,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected journal is not an opening balance journal.");
        }

        if (!string.Equals(
                originalOpeningJournal.Status,
                JournalEntryStatuses.Posted,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only posted opening balance journals can be adjusted.");
        }

        var businessDate = input.AdjustmentBusinessDate.Date;
        var postingDate = input.PostingDate.Date;
        await EnsureDateIsOpenForPostingAsync(
            businessId,
            businessDate,
            "Adjustment business date",
            cancellationToken);
        await EnsureDateIsOpenForPostingAsync(
            businessId,
            postingDate,
            "Adjustment posting date",
            cancellationToken);

        var lines = await MapAndValidateOpeningLinesAsync(
            businessId,
            input.Lines,
            cancellationToken);
        var reason = input.Reason.Trim();
        var description = string.IsNullOrWhiteSpace(input.Description)
            ? $"Opening balance adjustment for journal {openingBalanceJournalEntryId}. Reason: {reason}"
            : $"{input.Description.Trim()} (Reason: {reason})";
        var journal = new OpeningBalanceJournalInput(
            AccountingJournalTypes.OpeningBalanceAdjustment,
            postingDate,
            postingDate,
            businessDate,
            AccountingReferenceTypes.OpeningBalanceAdjustment,
            $"{openingBalanceJournalEntryId}:{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim(),
            description,
            null,
            lines);

        return await journalPoster.CreateOrGetAndPostAsync(
            businessId,
            actorUserId,
            journal,
            cancellationToken);
    }

    private async Task<IReadOnlyList<OpeningBalanceJournalLineInput>> MapAndValidateOpeningLinesAsync(
        long businessId,
        IReadOnlyCollection<OpeningBalanceLineInput>? lines,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOperationException("At least one opening balance line is required.");
        }

        if (lines.Any(line => line.OutletId.HasValue && line.WarehouseId.HasValue))
        {
            throw new InvalidOperationException(
                "Opening balance line cannot target both outlet and warehouse dimensions at the same time.");
        }

        var outletIds = lines
            .Where(line => line.OutletId.HasValue)
            .Select(line => line.OutletId!.Value)
            .Distinct()
            .ToList();
        if (outletIds.Count > 0)
        {
            var businessOutletIds = await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId && outletIds.Contains(outlet.Id))
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken);
            if (businessOutletIds.Count != outletIds.Count)
            {
                throw new InvalidOperationException(
                    "Opening balance line contains an outlet outside this business.");
            }
        }

        var warehouseIds = lines
            .Where(line => line.WarehouseId.HasValue)
            .Select(line => line.WarehouseId!.Value)
            .Distinct()
            .ToList();
        if (warehouseIds.Count > 0)
        {
            var businessWarehouseIds = await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse => warehouse.BusinessId == businessId && warehouseIds.Contains(warehouse.Id))
                .Select(warehouse => warehouse.Id)
                .ToListAsync(cancellationToken);
            if (businessWarehouseIds.Count != warehouseIds.Count)
            {
                throw new InvalidOperationException(
                    "Opening balance line contains a warehouse outside this business.");
            }
        }

        var accountIds = lines.Select(line => line.AccountId).Distinct().ToList();
        var accounts = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId && accountIds.Contains(account.Id))
            .ToListAsync(cancellationToken);
        var accountById = accounts.ToDictionary(account => account.Id);

        var missingIds = accountIds.Where(accountId => !accountById.ContainsKey(accountId)).ToArray();
        if (missingIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Opening balance line contains unknown account ID(s): {string.Join(", ", missingIds)}.");
        }

        var inactive = accountById.Values
            .Where(account => !account.IsActive)
            .Select(account => $"{account.AccountCode} ({account.Id})")
            .ToArray();
        if (inactive.Length > 0)
        {
            throw new InvalidOperationException(
                $"Opening balance line uses inactive account(s): {string.Join(", ", inactive)}.");
        }

        var disallowed = accountById.Values
            .Where(account => !AllowedAccountTypes.Contains(account.AccountType))
            .Select(account => $"{account.AccountCode} ({account.AccountType})")
            .ToArray();
        if (disallowed.Length > 0)
        {
            throw new InvalidOperationException(
                $"Opening balance supports only Balance Sheet account types ({ChartAccountTypes.Asset}, {ChartAccountTypes.Liability}, {ChartAccountTypes.Equity}). Invalid account(s): {string.Join(", ", disallowed)}.");
        }

        return lines.Select(line => new OpeningBalanceJournalLineInput(
            line.AccountId,
            line.DebitAmount,
            line.CreditAmount,
            line.OutletId,
            line.WarehouseId,
            string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim())).ToArray();
    }

    private async Task EnsureDateIsOpenForPostingAsync(
        long businessId,
        DateTime date,
        string label,
        CancellationToken cancellationToken)
    {
        var normalized = date.Date;
        var year = await dbContext.FinancialYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                financialYear => financialYear.BusinessId == businessId
                    && financialYear.StartDate.Date <= normalized
                    && financialYear.EndDate.Date >= normalized,
                cancellationToken);
        if (year is null)
        {
            return;
        }

        if (year.IsClosed)
        {
            throw new InvalidOperationException($"{label} belongs to a closed financial year.");
        }

        var period = await dbContext.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                accountingPeriod => accountingPeriod.FinancialYearId == year.Id
                    && accountingPeriod.StartDate.Date <= normalized
                    && accountingPeriod.EndDate.Date >= normalized,
                cancellationToken);
        if (period is not null
            && (string.Equals(
                    period.Status,
                    AccountingPeriodStatuses.Closed,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    period.Status,
                    AccountingPeriodStatuses.Locked,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{label} belongs to a closed or locked accounting period.");
        }
    }

    private static string BuildMappingValidationMessage(
        IReadOnlyCollection<string> errors,
        string prefix)
    {
        var orderedErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(error => error)
            .ToArray();

        return orderedErrors.Length == 0
            ? prefix
            : $"{prefix} {string.Join(" ", orderedErrors.Select(error => $"[{error}]"))}";
    }
}
