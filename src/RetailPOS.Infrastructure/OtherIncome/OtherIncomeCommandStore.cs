using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.OtherIncome;

public sealed class OtherIncomeCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    ILogger<OtherIncomeCommandStore> logger,
    IOtherIncomeApprovalJournalPoster approvalJournalPoster,
    IOtherIncomePostingAccountResolver postingAccountResolver) : IOtherIncomeCommandStore
{
    private const string OtherIncomePostingAccountSlot = "other_income";
    private const string VatPayablePostingAccountSlot = "vat_payable";

    private static readonly HashSet<string> EditableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        OtherIncomeStatuses.Draft,
        OtherIncomeStatuses.Rejected
    };

    public async Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        OtherIncomeCreationInput input,
        CancellationToken cancellationToken)
    {
        await ValidateOptionalOutletAsync(businessId, input.OutletId, cancellationToken);
        var paymentAccount = await ValidateReceiptAccountAsync(
            businessId,
            input.PaymentAccountId,
            cancellationToken);
        ChartAccount? incomeAccount = null;
        if (input.IncomeAccountId.HasValue)
        {
            incomeAccount = await ValidateIncomeAccountAsync(
                businessId,
                input.IncomeAccountId.Value,
                cancellationToken);
        }

        var now = DateTime.UtcNow;
        var entry = new OtherIncomeEntry
        {
            BusinessId = businessId,
            OutletId = input.OutletId,
            IncomeDate = input.IncomeDate.Date,
            IncomeCategory = NormalizeRequired(input.IncomeCategory, "Income category", 120),
            IncomeAccountId = incomeAccount?.Id,
            Amount = input.Amount,
            VatRate = input.VatRate,
            VatAmount = input.VatAmount,
            TaxableAmount = input.VatAmount.HasValue
                ? input.Amount - input.VatAmount.Value
                : input.Amount,
            TaxCode = input.TaxCode,
            TaxInclusive = input.TaxInclusive,
            PaymentMethod = NormalizeOptional(input.PaymentMethod, 40),
            PaymentAccountId = paymentAccount?.Id,
            ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80),
            Description = NormalizeOptional(input.Description, 500),
            Status = OtherIncomeStatuses.Draft,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedBy = actorUserId,
            UpdatedAt = now
        };

        dbContext.OtherIncomeEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Other income {OtherIncomeId} created", entry.Id);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Other income created: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Create,
                    [
                        ("Status", null, entry.Status),
                        ("Amount", null, entry.Amount),
                        ("VatRate", null, entry.VatRate),
                        ("VatAmount", null, entry.VatAmount),
                        ("TaxableAmount", null, entry.TaxableAmount),
                        ("TaxCode", null, entry.TaxCode),
                        ("IncomeCategory", null, entry.IncomeCategory),
                        ("IncomeAccountId", null, entry.IncomeAccountId),
                        ("PaymentAccountId", null, entry.PaymentAccountId),
                        ("OutletId", null, entry.OutletId),
                        ("ReferenceNumber", null, entry.ReferenceNumber)
                    ])
            ]
        }, cancellationToken);

        return entry.Id;
    }

    public async Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long entryId,
        OtherIncomeCreationInput input,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.OtherIncomeEntries.FirstOrDefaultAsync(
                otherIncome => otherIncome.BusinessId == businessId && otherIncome.Id == entryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Other income entry with ID {entryId} not found.");
        if (!EditableStatuses.Contains(entry.Status))
        {
            throw new InvalidOperationException(
                $"Other income entries in status '{entry.Status}' cannot be edited.");
        }

        await ValidateOptionalOutletAsync(businessId, input.OutletId, cancellationToken);
        var paymentAccount = await ValidateReceiptAccountAsync(
            businessId,
            input.PaymentAccountId,
            cancellationToken);
        ChartAccount? incomeAccount = null;
        if (input.IncomeAccountId.HasValue)
        {
            incomeAccount = await ValidateIncomeAccountAsync(
                businessId,
                input.IncomeAccountId.Value,
                cancellationToken);
        }

        var previousAmount = entry.Amount;
        var previousVatRate = entry.VatRate;
        var previousVatAmount = entry.VatAmount;
        var previousTaxableAmount = entry.TaxableAmount;
        var previousTaxCode = entry.TaxCode;
        var previousTaxInclusive = entry.TaxInclusive;
        var previousIncomeCategory = entry.IncomeCategory;
        var previousIncomeAccountId = entry.IncomeAccountId;
        var previousPaymentAccountId = entry.PaymentAccountId;
        var previousOutletId = entry.OutletId;
        var previousReferenceNumber = entry.ReferenceNumber;

        entry.OutletId = input.OutletId;
        entry.IncomeDate = input.IncomeDate.Date;
        entry.IncomeCategory = NormalizeRequired(input.IncomeCategory, "Income category", 120);
        entry.IncomeAccountId = incomeAccount?.Id;
        entry.Amount = input.Amount;
        entry.VatRate = input.VatRate;
        entry.VatAmount = input.VatAmount;
        entry.TaxableAmount = input.VatAmount.HasValue
            ? input.Amount - input.VatAmount.Value
            : input.Amount;
        entry.TaxCode = input.TaxCode;
        entry.TaxInclusive = input.TaxInclusive;
        entry.PaymentMethod = NormalizeOptional(input.PaymentMethod, 40);
        entry.PaymentAccountId = paymentAccount?.Id;
        entry.ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80);
        entry.Description = NormalizeOptional(input.Description, 500);
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = actorUserId;
        entry.RejectedAt = null;
        entry.RejectedBy = null;
        entry.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Other income {OtherIncomeId} updated", entry.Id);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Other income updated: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", null, entry.Status),
                        ("Amount", previousAmount, entry.Amount),
                        ("VatRate", previousVatRate, entry.VatRate),
                        ("VatAmount", previousVatAmount, entry.VatAmount),
                        ("TaxableAmount", previousTaxableAmount, entry.TaxableAmount),
                        ("TaxCode", previousTaxCode, entry.TaxCode),
                        ("TaxInclusive", previousTaxInclusive, entry.TaxInclusive),
                        ("IncomeCategory", previousIncomeCategory, entry.IncomeCategory),
                        ("IncomeAccountId", previousIncomeAccountId, entry.IncomeAccountId),
                        ("PaymentAccountId", previousPaymentAccountId, entry.PaymentAccountId),
                        ("OutletId", previousOutletId, entry.OutletId),
                        ("ReferenceNumber", previousReferenceNumber, entry.ReferenceNumber)
                    ])
            ]
        }, cancellationToken);

        return entry.Id;
    }

    public async Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long entryId,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.OtherIncomeEntries.FirstOrDefaultAsync(
                otherIncome => otherIncome.BusinessId == businessId && otherIncome.Id == entryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Other income entry with ID {entryId} not found.");
        if (!EditableStatuses.Contains(entry.Status))
        {
            throw new InvalidOperationException(
                $"Only Draft or Rejected other income entries can be submitted. Current status: {entry.Status}.");
        }

        entry.Status = OtherIncomeStatuses.Submitted;
        entry.SubmittedAt = DateTime.UtcNow;
        entry.SubmittedBy = actorUserId;
        entry.UpdatedAt = entry.SubmittedAt.Value;
        entry.UpdatedBy = actorUserId;
        entry.RejectedAt = null;
        entry.RejectedBy = null;
        entry.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Other income submitted: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Update,
                    [("Status", OtherIncomeStatuses.Draft, OtherIncomeStatuses.Submitted)])
            ]
        }, cancellationToken);

        return entry.Id;
    }

    public async Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long entryId,
        long? incomeAccountId,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.OtherIncomeEntries.FirstOrDefaultAsync(
                otherIncome => otherIncome.BusinessId == businessId && otherIncome.Id == entryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Other income entry with ID {entryId} not found.");
        if (!string.Equals(
                entry.Status,
                OtherIncomeStatuses.Submitted,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted other income entries can be approved. Current status: {entry.Status}.");
        }

        await EnsureDateIsOpenForApprovalPostingAsync(
            businessId,
            entry.IncomeDate.Date,
            cancellationToken);
        var paymentAccount = await ValidateReceiptAccountAsync(
                businessId,
                entry.PaymentAccountId,
                cancellationToken)
            ?? throw new InvalidOperationException("Receipt account is required before approval.");

        var resolvedIncomeAccountId = incomeAccountId
            ?? entry.IncomeAccountId
            ?? await postingAccountResolver.ResolveAccountIdAsync(
                businessId,
                OtherIncomePostingAccountSlot,
                entry.OutletId,
                cancellationToken);
        var resolvedIncomeAccount = await ValidateIncomeAccountAsync(
            businessId,
            resolvedIncomeAccountId,
            cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await LockOtherIncomeRowAsync(businessId, entryId, cancellationToken);
        await dbContext.Entry(entry).ReloadAsync(cancellationToken);
        if (!string.Equals(
                entry.Status,
                OtherIncomeStatuses.Submitted,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted other income entries can be approved. Current status: {entry.Status}.");
        }

        var vatAmount = entry.VatAmount.GetValueOrDefault();
        var taxableAmount = entry.TaxableAmount ?? entry.Amount;
        var lines = new List<OtherIncomeApprovalJournalLineInput>
        {
            new(
                paymentAccount.Id,
                entry.Description,
                taxableAmount + vatAmount,
                0,
                entry.OutletId),
            new(
                resolvedIncomeAccount.Id,
                entry.Description,
                0,
                taxableAmount,
                entry.OutletId)
        };
        if (vatAmount > 0m)
        {
            var outputVatAccountId = await postingAccountResolver.ResolveAccountIdAsync(
                businessId,
                VatPayablePostingAccountSlot,
                entry.OutletId,
                cancellationToken);
            lines.Add(new OtherIncomeApprovalJournalLineInput(
                outputVatAccountId,
                "Output VAT",
                0,
                vatAmount,
                entry.OutletId));
        }

        var journalEntryId = await approvalJournalPoster.CreateOrGetAndPostAsync(
            businessId,
            actorUserId,
            new OtherIncomeApprovalJournalInput(
                entry.IncomeDate.Date,
                entry.IncomeDate.Date,
                entry.IncomeDate.Date,
                "OtherIncome",
                entry.Id.ToString(),
                entry.ReferenceNumber,
                string.IsNullOrWhiteSpace(entry.Description)
                    ? $"Other income {entry.Id}"
                    : entry.Description,
                entry.OutletId,
                lines),
            cancellationToken);

        entry.IncomeAccountId = resolvedIncomeAccount.Id;
        entry.JournalEntryId = journalEntryId;
        entry.Status = OtherIncomeStatuses.Approved;
        entry.ApprovedAt = DateTime.UtcNow;
        entry.ApprovedBy = actorUserId;
        entry.UpdatedAt = entry.ApprovedAt.Value;
        entry.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Other income approved: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", OtherIncomeStatuses.Submitted, OtherIncomeStatuses.Approved),
                        ("IncomeAccountId", null, entry.IncomeAccountId),
                        ("JournalEntryId", null, entry.JournalEntryId)
                    ])
            ]
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Other income {OtherIncomeId} approved", entry.Id);
        return entry.Id;
    }

    private async Task ValidateOptionalOutletAsync(
        long businessId,
        long? outletId,
        CancellationToken cancellationToken)
    {
        if (!outletId.HasValue)
            return;

        if (!await dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == outletId.Value && outlet.BusinessId == businessId,
                cancellationToken))
        {
            throw new InvalidOperationException("Outlet not found in this business.");
        }
    }

    private async Task<ChartAccount> ValidateIncomeAccountAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts.FirstOrDefaultAsync(
                chartAccount => chartAccount.BusinessId == businessId && chartAccount.Id == accountId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Income account {accountId} was not found in this business.");

        if (!account.IsActive)
            throw new InvalidOperationException($"Income account '{account.AccountCode}' is inactive.");
        if (!string.Equals(
                account.AccountType,
                ChartAccountTypes.Income,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Account '{account.AccountCode}' is not a valid other income posting account.");
        }

        return account;
    }

    private async Task<ChartAccount?> ValidateReceiptAccountAsync(
        long businessId,
        long? accountId,
        CancellationToken cancellationToken)
    {
        if (!accountId.HasValue)
            return null;

        var account = await dbContext.ChartAccounts.FirstOrDefaultAsync(
                chartAccount => chartAccount.BusinessId == businessId
                    && chartAccount.Id == accountId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Receipt account {accountId.Value} was not found in this business.");

        if (!account.IsActive)
            throw new InvalidOperationException($"Receipt account '{account.AccountCode}' is inactive.");
        if (!string.Equals(
                account.AccountType,
                ChartAccountTypes.Asset,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Account '{account.AccountCode}' is not a valid receipt account for other income.");
        }

        return account;
    }

    public async Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long entryId,
        string reason,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.OtherIncomeEntries.FirstOrDefaultAsync(
                otherIncome => otherIncome.BusinessId == businessId && otherIncome.Id == entryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Other income entry with ID {entryId} not found.");
        if (!string.Equals(
                entry.Status,
                OtherIncomeStatuses.Submitted,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted other income entries can be rejected. Current status: {entry.Status}.");
        }

        var normalizedReason = NormalizeRequired(reason, "Rejection reason", 500);
        var now = DateTime.UtcNow;
        entry.Status = OtherIncomeStatuses.Rejected;
        entry.RejectedAt = now;
        entry.RejectedBy = actorUserId;
        entry.RejectionReason = normalizedReason;
        entry.UpdatedAt = now;
        entry.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Other income rejected: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", OtherIncomeStatuses.Submitted, OtherIncomeStatuses.Rejected),
                        ("RejectionReason", null, normalizedReason)
                    ])
            ]
        }, cancellationToken);

        return entry.Id;
    }

    public async Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long entryId,
        string reason,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.OtherIncomeEntries.FirstOrDefaultAsync(
                otherIncome => otherIncome.BusinessId == businessId && otherIncome.Id == entryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Other income entry with ID {entryId} not found.");
        if (string.Equals(
                entry.Status,
                OtherIncomeStatuses.Approved,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                entry.Status,
                OtherIncomeStatuses.Cancelled,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Other income entries in status '{entry.Status}' cannot be cancelled.");
        }

        var normalizedReason = NormalizeRequired(reason, "Cancellation reason", 500);
        var oldStatus = entry.Status;
        var now = DateTime.UtcNow;
        entry.Status = OtherIncomeStatuses.Cancelled;
        entry.CancelledAt = now;
        entry.CancelledBy = actorUserId;
        entry.CancellationReason = normalizedReason;
        entry.UpdatedAt = now;
        entry.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Other income cancelled: {entry.Id}",
            PrimaryEntity = ("OtherIncomeEntry", entry.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "OtherIncomeEntry",
                    entry.Id.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", oldStatus, OtherIncomeStatuses.Cancelled),
                        ("CancellationReason", null, normalizedReason)
                    ])
            ]
        }, cancellationToken);

        return entry.Id;
    }

    private async Task EnsureDateIsOpenForApprovalPostingAsync(
        long businessId,
        DateTime postingDate,
        CancellationToken cancellationToken)
    {
        var normalized = postingDate.Date;
        var year = await dbContext.FinancialYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                financialYear => financialYear.BusinessId == businessId
                    && financialYear.StartDate.Date <= normalized
                    && financialYear.EndDate.Date >= normalized,
                cancellationToken);
        if (year is null)
            throw new InvalidOperationException(
                $"Other income date {normalized:yyyy-MM-dd} does not belong to any financial year.");
        if (year.IsClosed)
            throw new InvalidOperationException(
                $"Other income date {normalized:yyyy-MM-dd} belongs to a closed financial year.");

        var period = await dbContext.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                accountingPeriod => accountingPeriod.FinancialYearId == year.Id
                    && accountingPeriod.StartDate.Date <= normalized
                    && accountingPeriod.EndDate.Date >= normalized,
                cancellationToken);
        if (period is null)
            throw new InvalidOperationException(
                $"Other income date {normalized:yyyy-MM-dd} does not belong to any accounting period.");
        if (!string.Equals(
                period.Status,
                AccountingPeriodStatuses.Open,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Other income date {normalized:yyyy-MM-dd} belongs to accounting period '{period.Name}' which is not open.");
        }
    }

    private async Task LockOtherIncomeRowAsync(
        long businessId,
        long entryId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()
            || string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM other_income_entries WHERE business_id = {businessId} AND id = {entryId} FOR UPDATE",
            cancellationToken);
    }

    private static string NormalizeRequired(string? value, string fieldName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{fieldName} is required.");
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} exceeds maximum length of {maxLength}.");
        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"Field exceeds maximum length of {maxLength}.");
        return trimmed;
    }
}
