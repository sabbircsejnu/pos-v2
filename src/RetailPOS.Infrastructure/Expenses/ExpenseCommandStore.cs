using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

public sealed class ExpenseCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    ILogger<ExpenseCommandStore> logger,
    IExpenseApprovalJournalPoster approvalJournalPoster,
    IExpensePostingAccountResolver postingAccountResolver,
    IExpenseMakerCheckerPolicy makerCheckerPolicy)
    : IExpenseCommandStore
{
    public async Task<ExpenseCategoryCommandResult> CreateCategoryAsync(
        long businessId,
        long actorUserId,
        ExpenseCategoryCommandInput input,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequired(input.Name, "Expense category name", 120);
        var normalizedName = name.ToLowerInvariant();
        if (await dbContext.ExpenseCategories.AnyAsync(
                category => category.BusinessId == businessId
                    && category.Name.ToLower() == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"Expense category '{name}' already exists.");
        }

        var account = await GetExpenseAccountAsync(
            businessId,
            input.ExpenseAccountId,
            cancellationToken);
        var now = DateTime.UtcNow;
        var category = new ExpenseCategory
        {
            BusinessId = businessId,
            Name = name,
            ExpenseAccountId = account.Id,
            IsActive = input.IsActive,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };

        dbContext.ExpenseCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Expense category created: {category.Id}",
            PrimaryEntity = ("ExpenseCategory", category.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "ExpenseCategory",
                    category.Id.ToString(),
                    AuditActionType.Create,
                    [
                        ("Name", null, category.Name),
                        ("ExpenseAccountId", null, category.ExpenseAccountId),
                        ("IsActive", null, category.IsActive)
                    ])
            ]
        }, cancellationToken);

        return new ExpenseCategoryCommandResult(
            category.Id,
            category.Name,
            category.ExpenseAccountId,
            account.AccountCode,
            account.AccountName,
            category.IsActive);
    }

    public async Task<ExpenseCategoryCommandResult> UpdateCategoryAsync(
        long businessId,
        long actorUserId,
        long categoryId,
        ExpenseCategoryCommandInput input,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ExpenseCategories
            .FirstOrDefaultAsync(
                expenseCategory => expenseCategory.BusinessId == businessId
                    && expenseCategory.Id == categoryId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Expense category with ID {categoryId} not found.");

        var name = NormalizeRequired(input.Name, "Expense category name", 120);
        var normalizedName = name.ToLowerInvariant();
        if (await dbContext.ExpenseCategories.AnyAsync(
                expenseCategory => expenseCategory.BusinessId == businessId
                    && expenseCategory.Id != categoryId
                    && expenseCategory.Name.ToLower() == normalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException($"Expense category '{name}' already exists.");
        }

        var account = await GetExpenseAccountAsync(
            businessId,
            input.ExpenseAccountId,
            cancellationToken);
        var oldName = category.Name;
        var oldExpenseAccountId = category.ExpenseAccountId;
        var oldIsActive = category.IsActive;
        var now = DateTime.UtcNow;

        category.Name = name;
        category.ExpenseAccountId = account.Id;
        category.IsActive = input.IsActive;
        category.UpdatedAt = now;
        category.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense category updated: {category.Id}",
            PrimaryEntity = ("ExpenseCategory", category.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "ExpenseCategory",
                    category.Id.ToString(),
                    AuditActionType.Update,
                    [
                        ("Name", oldName, category.Name),
                        ("ExpenseAccountId", oldExpenseAccountId, category.ExpenseAccountId),
                        ("IsActive", oldIsActive, category.IsActive)
                    ])
            ]
        }, cancellationToken);

        return new ExpenseCategoryCommandResult(
            category.Id,
            category.Name,
            category.ExpenseAccountId,
            account.AccountCode,
            account.AccountName,
            category.IsActive);
    }

    public async Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        ExpenseCreationInput input,
        CancellationToken cancellationToken)
    {
        var category = await GetActiveExpenseCategoryAsync(
                businessId,
                input.ExpenseCategoryId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Expense category with ID {input.ExpenseCategoryId} is not active or was not found.");

        var paymentAccount = await GetPaymentAccountAsync(
            businessId,
            input.PaymentAccountId,
            cancellationToken);

        await ValidateOptionalOutletAsync(businessId, input.OutletId, cancellationToken);
        var paymentSource = NormalizePaymentSource(input.PaymentSource, input.OutletId);

        var now = DateTime.UtcNow;
        var expense = new Expense
        {
            BusinessId = businessId,
            PaymentSource = paymentSource,
            Category = category.Name,
            ExpenseCategoryId = category.Id,
            Amount = input.Amount,
            VatRate = input.VatRate,
            VatAmount = input.VatAmount,
            TaxableAmount = input.VatAmount.HasValue
                ? input.Amount - input.VatAmount.Value
                : input.Amount,
            TaxCode = input.TaxCode,
            TaxInclusive = input.TaxInclusive,
            PaymentMethodId = NormalizeOptional(input.PaymentMethodId, 50),
            PaymentAccountId = paymentAccount.Id,
            ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80),
            Description = NormalizeOptional(input.Description, 500),
            AttachmentPath = NormalizeOptional(input.AttachmentPath, 500),
            ExpenseDate = input.ExpenseDate.Date,
            OutletId = input.OutletId,
            Status = ExpenseStatuses.Draft,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedBy = actorUserId,
            UpdatedAt = now
        };

        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);
        expense.ExpenseNumber = $"EXP-{expense.Id:D6}";
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Expense {ExpenseId} created", expense.Id);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Expense created: {expense.Id}",
            PrimaryEntity = ("Expense", expense.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expense.Id.ToString(),
                    AuditActionType.Create,
                    [
                        ("Status", null, expense.Status),
                        ("Amount", null, expense.Amount),
                        ("VatRate", null, expense.VatRate),
                        ("VatAmount", null, expense.VatAmount),
                        ("TaxableAmount", null, expense.TaxableAmount),
                        ("TaxCode", null, expense.TaxCode),
                        ("ExpenseCategoryId", null, expense.ExpenseCategoryId),
                        ("PaymentAccountId", null, expense.PaymentAccountId),
                        ("OutletId", null, expense.OutletId),
                        ("ReferenceNumber", null, expense.ReferenceNumber)
                    ])
            ]
        }, cancellationToken);

        return expense.Id;
    }

    public async Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        ExpenseUpdateInput input,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (!string.Equals(expense.Status, ExpenseStatuses.Draft, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(expense.Status, ExpenseStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expenses in status '{expense.Status}' cannot be edited.");

        var category = await GetActiveExpenseCategoryAsync(
            businessId,
            input.ExpenseCategoryId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Expense category with ID {input.ExpenseCategoryId} is not active or was not found.");
        var paymentAccount = await GetPaymentAccountAsync(
            businessId,
            input.PaymentAccountId,
            cancellationToken);
        await ValidateOptionalOutletAsync(businessId, input.OutletId, cancellationToken);
        var paymentSource = NormalizePaymentSource(input.PaymentSource, input.OutletId);

        var previousAmount = expense.Amount;
        var previousVatRate = expense.VatRate;
        var previousVatAmount = expense.VatAmount;
        var previousTaxableAmount = expense.TaxableAmount;
        var previousTaxCode = expense.TaxCode;
        var previousTaxInclusive = expense.TaxInclusive;
        var previousCategoryId = expense.ExpenseCategoryId;
        var previousPaymentAccountId = expense.PaymentAccountId;
        var previousOutletId = expense.OutletId;
        var previousReferenceNumber = expense.ReferenceNumber;

        expense.Category = category.Name;
        expense.ExpenseCategoryId = category.Id;
        expense.Amount = input.Amount;
        expense.VatRate = input.VatRate;
        expense.VatAmount = input.VatAmount;
        expense.TaxableAmount = input.VatAmount.HasValue
            ? input.Amount - input.VatAmount.Value
            : input.Amount;
        expense.TaxCode = input.TaxCode;
        expense.TaxInclusive = input.TaxInclusive;
        expense.PaymentMethodId = NormalizeOptional(input.PaymentMethodId, 50);
        expense.PaymentAccountId = paymentAccount.Id;
        expense.ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80);
        expense.Description = NormalizeOptional(input.Description, 500);
        expense.AttachmentPath = NormalizeOptional(input.AttachmentPath, 500);
        expense.ExpenseDate = input.ExpenseDate.Date;
        expense.OutletId = input.OutletId;
        expense.PaymentSource = paymentSource;
        expense.UpdatedAt = DateTime.UtcNow;
        expense.UpdatedBy = actorUserId;
        expense.RejectedAt = null;
        expense.RejectedBy = null;
        expense.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Expense {ExpenseId} updated", expenseId);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense updated: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", expense.Status, expense.Status),
                        ("Amount", previousAmount, expense.Amount),
                        ("VatRate", previousVatRate, expense.VatRate),
                        ("VatAmount", previousVatAmount, expense.VatAmount),
                        ("TaxableAmount", previousTaxableAmount, expense.TaxableAmount),
                        ("TaxCode", previousTaxCode, expense.TaxCode),
                        ("TaxInclusive", previousTaxInclusive, expense.TaxInclusive),
                        ("ExpenseCategoryId", previousCategoryId, expense.ExpenseCategoryId),
                        ("PaymentAccountId", previousPaymentAccountId, expense.PaymentAccountId),
                        ("OutletId", previousOutletId, expense.OutletId),
                        ("ReferenceNumber", previousReferenceNumber, expense.ReferenceNumber)
                    ])
            ]
        }, cancellationToken);

        return expense.Id;
    }

    public async Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (!string.Equals(expense.Status, ExpenseStatuses.Draft, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(expense.Status, ExpenseStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Draft or Rejected expenses can be submitted. Current status: {expense.Status}.");
        }

        var submittedAt = DateTime.UtcNow;
        expense.Status = ExpenseStatuses.Submitted;
        expense.SubmittedAt = submittedAt;
        expense.SubmittedBy = actorUserId;
        expense.UpdatedAt = submittedAt;
        expense.UpdatedBy = actorUserId;
        expense.RejectedAt = null;
        expense.RejectedBy = null;
        expense.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense submitted: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Update,
                    [("Status", ExpenseStatuses.Draft, ExpenseStatuses.Submitted)])
            ]
        }, cancellationToken);

        return expense.Id;
    }

    public async Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        long? expenseAccountId,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .Include(entry => entry.ExpenseCategory)
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (!string.Equals(expense.Status, ExpenseStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted expenses can be approved. Current status: {expense.Status}.");
        }

        // D7: for POS Drawer / Petty Cash the approver must be independent of the creator and the submitter.
        var selfApproved = await makerCheckerPolicy.EvaluateAsync(expense, actorUserId, cancellationToken);

        var category = expense.ExpenseCategory
            ?? await dbContext.ExpenseCategories
                .Include(expenseCategory => expenseCategory.ExpenseAccount)
                .FirstOrDefaultAsync(
                    expenseCategory => expenseCategory.BusinessId == businessId
                        && expenseCategory.Id == (expense.ExpenseCategoryId ?? 0)
                        && expenseCategory.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Expense category with ID {expense.ExpenseCategoryId ?? 0} is not active or was not found.");
        if (!category.IsActive)
        {
            throw new InvalidOperationException($"Expense category '{category.Name}' is inactive.");
        }

        var resolvedExpenseAccount = await GetExpenseAccountAsync(
            businessId,
            expenseAccountId ?? category.ExpenseAccountId,
            cancellationToken);
        var paymentAccountId = expense.PaymentAccountId
            ?? throw new InvalidOperationException("Payment account is required before approval.");
        var paymentAccount = await GetPaymentAccountAsync(
            businessId,
            paymentAccountId,
            cancellationToken);
        var supplierPayableAccountId = await postingAccountResolver.ResolveAccountIdAsync(
            businessId,
            "supplier_payable",
            expense.OutletId,
            cancellationToken);

        await EnsureDateIsOpenForApprovalPostingAsync(
            businessId,
            expense.ExpenseDate.Date,
            cancellationToken);

        if (paymentAccount.Id == supplierPayableAccountId)
        {
            throw new InvalidOperationException(
                "Supplier payable cannot be used as the payment account for RM-05 expense approval until supplier-linked allocation workflow exists.");
        }

        var vatAmount = expense.VatAmount.GetValueOrDefault();
        var taxableAmount = expense.TaxableAmount ?? expense.Amount;
        var lines = new List<ExpenseApprovalJournalLineInput>
        {
            new(resolvedExpenseAccount.Id, expense.Description, taxableAmount, 0, expense.OutletId),
            new(paymentAccount.Id, expense.Description, 0, taxableAmount + vatAmount, expense.OutletId)
        };
        if (vatAmount > 0m)
        {
            var inputVatAccountId = await postingAccountResolver.ResolveAccountIdAsync(
                businessId,
                "vat_receivable",
                expense.OutletId,
                cancellationToken);
            lines.Add(new ExpenseApprovalJournalLineInput(
                inputVatAccountId,
                "Input VAT",
                vatAmount,
                0,
                expense.OutletId));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await LockExpenseRowAsync(businessId, expenseId, cancellationToken);
        await dbContext.Entry(expense).ReloadAsync(cancellationToken);
        if (!string.Equals(expense.Status, ExpenseStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted expenses can be approved. Current status: {expense.Status}.");
        }

        var journalEntryId = await approvalJournalPoster.CreateOrGetAndPostAsync(
            businessId,
            actorUserId,
            new ExpenseApprovalJournalInput(
                expense.ExpenseDate.Date,
                expense.ExpenseDate.Date,
                expense.ExpenseDate.Date,
                "Expense",
                expense.Id.ToString(),
                expense.ExpenseNumber,
                string.IsNullOrWhiteSpace(expense.Description)
                    ? $"Expense {expense.ExpenseNumber ?? expense.Id.ToString()}"
                    : expense.Description,
                expense.OutletId,
                lines),
            cancellationToken);

        expense.ExpenseAccountId = resolvedExpenseAccount.Id;
        expense.JournalEntryId = journalEntryId;
        expense.Status = ExpenseStatuses.Approved;
        expense.SelfApprovedUnderOwnerException = selfApproved;
        expense.ApprovedAt = DateTime.UtcNow;
        expense.ApprovedBy = actorUserId;
        expense.UpdatedAt = expense.ApprovedAt.Value;
        expense.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense approved: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", ExpenseStatuses.Submitted, ExpenseStatuses.Approved),
                        ("ExpenseAccountId", null, expense.ExpenseAccountId),
                        ("JournalEntryId", null, expense.JournalEntryId),
                        ("PaymentSource", null, expense.PaymentSource),
                        ("SelfApprovedUnderOwnerException", false, expense.SelfApprovedUnderOwnerException)
                    ])
            ]
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Expense {ExpenseId} approved", expenseId);
        return expense.Id;
    }

    public async Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        string reason,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (!string.Equals(expense.Status, ExpenseStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Only Submitted expenses can be rejected. Current status: {expense.Status}.");
        }

        var normalizedReason = NormalizeRequired(reason, "Rejection reason", 500);
        expense.Status = ExpenseStatuses.Rejected;
        expense.RejectedAt = DateTime.UtcNow;
        expense.RejectedBy = actorUserId;
        expense.RejectionReason = normalizedReason;
        expense.UpdatedAt = expense.RejectedAt.Value;
        expense.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense rejected: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", ExpenseStatuses.Submitted, ExpenseStatuses.Rejected),
                        ("RejectionReason", null, normalizedReason)
                    ])
            ]
        }, cancellationToken);
        return expense.Id;
    }

    public async Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        string reason,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (string.Equals(expense.Status, ExpenseStatuses.Approved, StringComparison.OrdinalIgnoreCase)
            || string.Equals(expense.Status, ExpenseStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expenses in status '{expense.Status}' cannot be cancelled.");
        }

        var normalizedReason = NormalizeRequired(reason, "Cancellation reason", 500);
        var oldStatus = expense.Status;
        expense.Status = ExpenseStatuses.Cancelled;
        expense.CancelledAt = DateTime.UtcNow;
        expense.CancelledBy = actorUserId;
        expense.CancellationReason = normalizedReason;
        expense.UpdatedAt = expense.CancelledAt.Value;
        expense.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Expense cancelled: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Update,
                    [
                        ("Status", oldStatus, ExpenseStatuses.Cancelled),
                        ("CancellationReason", null, normalizedReason)
                    ])
            ]
        }, cancellationToken);
        return expense.Id;
    }

    public async Task<long> DeleteAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId && entry.Id == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        if (!string.Equals(expense.Status, ExpenseStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only Draft expenses can be deleted.");

        dbContext.Expenses.Remove(expense);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Delete,
            Module = AuditModule.Accounts,
            Summary = $"Expense deleted: {expenseId}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "Expense",
                    expenseId.ToString(),
                    AuditActionType.Delete,
                    [("DeletedBy", null, actorUserId)])
            ]
        }, cancellationToken);
        logger.LogInformation("Expense {ExpenseId} deleted", expenseId);
        return expenseId;
    }

    private async Task LockExpenseRowAsync(
        long businessId,
        long expenseId,
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
            $"SELECT id FROM expenses WHERE business_id = {businessId} AND id = {expenseId} FOR UPDATE",
            cancellationToken);
    }

    private async Task<ChartAccount> GetExpenseAccountAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts
            .FirstOrDefaultAsync(
                chartAccount => chartAccount.BusinessId == businessId && chartAccount.Id == accountId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Expense account {accountId} was not found in this business.");
        if (!account.IsActive)
            throw new InvalidOperationException($"Expense account '{account.AccountCode}' is inactive.");
        if (!string.Equals(account.AccountType, ChartAccountTypes.Expense, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Account '{account.AccountCode}' is not a valid expense posting account.");
        return account;
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
                $"Expense date {normalized:yyyy-MM-dd} does not belong to any financial year.");
        if (year.IsClosed)
            throw new InvalidOperationException(
                $"Expense date {normalized:yyyy-MM-dd} belongs to a closed financial year.");

        var period = await dbContext.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                accountingPeriod => accountingPeriod.FinancialYearId == year.Id
                    && accountingPeriod.StartDate.Date <= normalized
                    && accountingPeriod.EndDate.Date >= normalized,
                cancellationToken);
        if (period is null)
            throw new InvalidOperationException(
                $"Expense date {normalized:yyyy-MM-dd} does not belong to any accounting period.");
        if (!string.Equals(period.Status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expense date {normalized:yyyy-MM-dd} belongs to accounting period '{period.Name}' which is not open.");
    }

    private Task<ExpenseCategory?> GetActiveExpenseCategoryAsync(
        long businessId,
        long categoryId,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseCategories
            .Include(expenseCategory => expenseCategory.ExpenseAccount)
            .FirstOrDefaultAsync(
                expenseCategory => expenseCategory.BusinessId == businessId
                    && expenseCategory.Id == categoryId
                    && expenseCategory.IsActive,
                cancellationToken);

    private static string? NormalizePaymentSource(string? source, long? outletId)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var normalized = source.Trim().ToLowerInvariant();
        if (!ExpensePaymentSources.All.Contains(normalized))
            throw new InvalidOperationException("Payment source must be bank, petty_cash or pos_drawer.");
        if (normalized == ExpensePaymentSources.PosDrawer && !outletId.HasValue)
            throw new InvalidOperationException("A POS Drawer expense needs the outlet whose cash drawer pays it.");
        return normalized;
    }

    private async Task<ChartAccount> GetPaymentAccountAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts
            .FirstOrDefaultAsync(
                chartAccount => chartAccount.BusinessId == businessId
                    && chartAccount.Id == accountId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Payment account {accountId} was not found in this business.");
        if (!account.IsActive)
            throw new InvalidOperationException(
                $"Payment account '{account.AccountCode}' is inactive.");
        if (!string.Equals(account.AccountType, ChartAccountTypes.Asset, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(account.AccountType, ChartAccountTypes.Liability, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Account '{account.AccountCode}' is not a valid payment or payable account.");
        return account;
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
            throw new InvalidOperationException("Outlet not found in this business.");
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

    private static string NormalizeRequired(string? value, string fieldName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{fieldName} is required.");
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} exceeds maximum length of {maxLength}.");
        return trimmed;
    }
}
