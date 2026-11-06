using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class ExpenseWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ExpenseWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Category_Create_Expense_Submit_Approve_Lifecycle_Works()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,accounting.expenses.submit,accounting.expenses.approve");

        var categoryResponse = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Utilities",
            expenseAccountId = 7001,
            isActive = true
        });

        Assert.Equal(HttpStatusCode.OK, categoryResponse.StatusCode);
        var category = await ReadData<ExpenseCategoryResponse>(categoryResponse);
        Assert.NotNull(category);

        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 88m,
            paymentAccountId = 1021,
            expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            description = "Internet bill",
            outletId = 11
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);
        Assert.Equal(ExpenseStatuses.Draft, created!.Status);
        Assert.Matches(@"^\d{8}-OUT11-0001$", created.ExpenseNumber);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounting/expenses/{created.Id}",
            new
            {
                expenseCategoryId = category.Id,
                amount = 99m,
                paymentAccountId = 1021,
                expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                description = "Internet bill revised",
                referenceNumber = "INV-12",
                outletId = 11
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadData<ExpenseResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal(ExpenseStatuses.Draft, updated!.Status);
        Assert.Equal(99m, updated.Amount);
        Assert.Equal("Internet bill revised", updated.Description);
        Assert.Equal("INV-12", updated.ReferenceNumber);

        var submitResponse = await client.PostAsync($"/api/accounting/expenses/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await ReadData<ExpenseResponse>(submitResponse);
        Assert.NotNull(submitted);
        Assert.Equal(ExpenseStatuses.Submitted, submitted!.Status);

        var approveResponse = await client.PostAsJsonAsync($"/api/accounting/expenses/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await ReadData<ExpenseResponse>(approveResponse);
        Assert.NotNull(approved);
        Assert.Equal(ExpenseStatuses.Approved, approved!.Status);
        Assert.NotNull(approved.JournalEntryId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persistedSubmission = await db.Expenses.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(101L, persistedSubmission.SubmittedBy);
        Assert.NotNull(persistedSubmission.SubmittedAt);

        var createAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("created", StringComparison.Ordinal));
        Assert.Equal(1L, createAudit.BusinessId);
        Assert.Equal(101L, createAudit.RealUserId);
        Assert.Contains(createAudit.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "Amount" && (x.NewValue?.Contains("88") ?? false));

        var updateAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("updated", StringComparison.Ordinal));
        Assert.Equal(1L, updateAudit.BusinessId);
        Assert.Equal(101L, updateAudit.RealUserId);
        Assert.Contains(updateAudit.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "Amount" && (x.OldValue?.Contains("88") ?? false) && (x.NewValue?.Contains("99") ?? false));

        var submitAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("submitted", StringComparison.Ordinal));
        Assert.Equal(1L, submitAudit.BusinessId);
        Assert.Equal(101L, submitAudit.RealUserId);
        Assert.Contains(submitAudit.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "Status");

        var approveAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("approved", StringComparison.Ordinal));
        Assert.Equal(1L, approveAudit.BusinessId);
        Assert.Equal(101L, approveAudit.RealUserId);
        Assert.Null(approveAudit.ActingUserId);
        Assert.NotEqual(default, approveAudit.CreatedAt);
        Assert.Contains(approveAudit.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "JournalEntryId" && (x.NewValue?.Contains(approved.JournalEntryId.Value.ToString()) ?? false));
    }

    [Fact]
    public async Task ExpenseCategorySearchDetailAndSummary_ReadsPreserveExistingResults()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            "accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,expenses.view");

        var categoryCreate = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Utilities",
            expenseAccountId = 7001,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, categoryCreate.StatusCode);
        var category = await ReadData<ExpenseCategoryResponse>(categoryCreate);
        Assert.NotNull(category);

        var categoriesResponse = await client.GetAsync("/api/accounting/expenses/categories");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categories = await ReadData<List<ExpenseCategoryResponse>>(categoriesResponse);
        Assert.NotNull(categories);
        Assert.Contains(categories!, item => item.Id == category!.Id && item.ExpenseAccountCode == "7000");

        var expenseDate = DateTime.UtcNow.Date;
        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 45m,
            paymentAccountId = 1021,
            expenseDate,
            description = "Monthly utilities",
            outletId = 11
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);

        var detailResponse = await client.GetAsync($"/api/accounting/expenses/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadData<ExpenseResponse>(detailResponse);
        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail!.Id);
        Assert.Equal("Utilities", detail.Category);

        var searchResponse = await client.GetAsync(
            "/api/accounting/expenses?category=utilities&status=Draft&outletId=11&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var search = await ReadData<ExpenseListResponse>(searchResponse);
        Assert.NotNull(search);
        Assert.Equal(1, search!.TotalCount);
        Assert.Equal(45m, search.TotalAmount);
        Assert.Equal(created.Id, Assert.Single(search.Expenses).Id);

        var postSearchResponse = await client.PostAsJsonAsync("/api/accounting/expenses/search", new
        {
            category = "Utilities",
            status = "Draft",
            outletId = 11,
            pageNumber = 1,
            pageSize = 10
        });
        Assert.Equal(HttpStatusCode.OK, postSearchResponse.StatusCode);
        var postSearch = await ReadData<ExpenseListResponse>(postSearchResponse);
        Assert.NotNull(postSearch);
        Assert.Equal(1, postSearch!.TotalCount);
        Assert.Equal(45m, postSearch.TotalAmount);

        var summaryResponse = await client.GetAsync("/api/accounting/expenses/summary");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await ReadData<ExpenseSummaryResponse>(summaryResponse);
        Assert.NotNull(summary);
        Assert.Equal(45m, summary!.TotalAmount);
        Assert.Equal(1, summary.Count);
        Assert.Equal(45m, summary.ByCategory["Utilities"]);

        var legacyListResponse = await client.GetAsync(
            $"/api/expenses?category=utilities&outletId=11&startDate={expenseDate:yyyy-MM-dd}&endDate={expenseDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, legacyListResponse.StatusCode);
        var legacyList = await ReadData<ExpenseListResponse>(legacyListResponse);
        Assert.NotNull(legacyList);
        Assert.Equal(created.Id, Assert.Single(legacyList!.Expenses).Id);

        var legacySearchResponse = await client.PostAsJsonAsync("/api/expenses/search", new
        {
            category = "Utilities",
            status = "Draft",
            outletId = 11,
            pageNumber = 1,
            pageSize = 10
        });
        Assert.Equal(HttpStatusCode.OK, legacySearchResponse.StatusCode);
        var legacySearch = await ReadData<ExpenseListResponse>(legacySearchResponse);
        Assert.NotNull(legacySearch);
        Assert.Equal(45m, legacySearch!.TotalAmount);

        var legacyDetailResponse = await client.GetAsync($"/api/expenses/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, legacyDetailResponse.StatusCode);
        var legacyDetail = await ReadData<ExpenseResponse>(legacyDetailResponse);
        Assert.NotNull(legacyDetail);
        Assert.Equal(created.Id, legacyDetail!.Id);

        var legacySummaryResponse = await client.GetAsync("/api/expenses/summary");
        Assert.Equal(HttpStatusCode.OK, legacySummaryResponse.StatusCode);
        var legacySummary = await ReadData<ExpenseSummaryResponse>(legacySummaryResponse);
        Assert.NotNull(legacySummary);
        Assert.Equal(45m, legacySummary!.TotalAmount);
    }

    [Fact]
    public async Task Category_Update_PreservesResponseAndBusinessActorAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.expenses.edit,accounting.expense_categories.manage");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Utilities",
            expenseAccountId = 7001,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadData<ExpenseCategoryResponse>(createResponse);
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounting/expenses/categories/{created!.Id}",
            new
            {
                name = "  Office supplies  ",
                expenseAccountId = 7001,
                isActive = false
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadData<ExpenseCategoryResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal("Office supplies", updated.Name);
        Assert.Equal(7001L, updated.ExpenseAccountId);
        Assert.Equal("7000", updated.ExpenseAccountCode);
        Assert.Equal("Utilities Expense", updated.ExpenseAccountName);
        Assert.False(updated.IsActive);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.ExpenseCategories.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("Office supplies", persisted.Name);
        Assert.False(persisted.IsActive);
        Assert.Equal(101L, persisted.UpdatedBy);
        Assert.NotEqual(default, persisted.UpdatedAt);

        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "ExpenseCategory"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("updated", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        var changes = audit.Entities.SelectMany(x => x.FieldChanges).ToArray();
        Assert.Contains(changes, x =>
            x.FieldName == "Name"
            && x.OldValue?.Contains("Utilities") == true
            && x.NewValue?.Contains("Office supplies") == true);
        Assert.Contains(changes, x =>
            x.FieldName == "IsActive"
            && x.OldValue?.Contains("true", StringComparison.OrdinalIgnoreCase) == true
            && x.NewValue?.Contains("false", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Approve_WithoutPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var fullClient = CreateClient("accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,accounting.expenses.submit,accounting.expenses.approve");

        var categoryResponse = await fullClient.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Supplies",
            expenseAccountId = 7001,
            isActive = true
        });
        var category = await ReadData<ExpenseCategoryResponse>(categoryResponse);
        Assert.NotNull(category);

        var createResponse = await fullClient.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 22m,
            paymentAccountId = 1021,
            expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            description = "Printer paper",
            outletId = 11
        });
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await fullClient.PostAsync($"/api/accounting/expenses/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var limitedClient = CreateClient("accounting.expenses.view");
        var approveResponse = await limitedClient.PostAsJsonAsync($"/api/accounting/expenses/{created.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.Forbidden, approveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(db.AuditEvents.Where(x => x.PrimaryEntityType == "Expense" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("approved", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reject_UsesSubmittedEligibilityAndRecordsNormalizedReasonAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,accounting.expenses.submit,accounting.expenses.reject");

        var categoryResponse = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Supplies",
            expenseAccountId = 7001,
            isActive = true
        });
        var category = await ReadData<ExpenseCategoryResponse>(categoryResponse);
        Assert.NotNull(category);

        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 22m,
            paymentAccountId = 1021,
            expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            description = "Printer paper",
            outletId = 11
        });
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/accounting/expenses/{created!.Id}/submit", null)).StatusCode);
        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/accounting/expenses/{created.Id}/reject",
            new { reason = "  Missing receipt  " });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = await ReadData<ExpenseResponse>(rejectResponse);
        Assert.NotNull(rejected);
        Assert.Equal(ExpenseStatuses.Rejected, rejected!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.Expenses.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("Missing receipt", persisted.RejectionReason);
        Assert.Equal(101L, persisted.RejectedBy);
        Assert.NotNull(persisted.RejectedAt);

        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        Assert.Contains(audit.Entities.SelectMany(x => x.FieldChanges), x =>
            x.FieldName == "RejectionReason" && (x.NewValue?.Contains("Missing receipt") ?? false));
    }

    [Fact]
    public async Task Cancel_Draft_RecordsNormalizedReasonAndStatusAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,accounting.expenses.cancel");

        var categoryResponse = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Supplies",
            expenseAccountId = 7001,
            isActive = true
        });
        var category = await ReadData<ExpenseCategoryResponse>(categoryResponse);
        Assert.NotNull(category);

        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 22m,
            paymentAccountId = 1021,
            expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            description = "Printer paper",
            outletId = 11
        });
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/accounting/expenses/{created!.Id}/cancel",
            new { reason = "  Entered in error  " });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await ReadData<ExpenseResponse>(cancelResponse);
        Assert.NotNull(cancelled);
        Assert.Equal(ExpenseStatuses.Cancelled, cancelled!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.Expenses.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("Entered in error", persisted.CancellationReason);
        Assert.Equal(101L, persisted.CancelledBy);
        Assert.NotNull(persisted.CancelledAt);

        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("cancelled", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        Assert.Contains(audit.Entities.SelectMany(x => x.FieldChanges), x =>
            x.FieldName == "Status" && x.OldValue?.Contains(ExpenseStatuses.Draft) == true);
        Assert.Contains(audit.Entities.SelectMany(x => x.FieldChanges), x =>
            x.FieldName == "CancellationReason" && (x.NewValue?.Contains("Entered in error") ?? false));
    }

    [Fact]
    public async Task Delete_Draft_PreservesResponseAndDeletionAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expense_categories.manage,expenses.view,expenses.delete");

        var categoryResponse = await client.PostAsJsonAsync("/api/accounting/expenses/categories", new
        {
            name = "Supplies",
            expenseAccountId = 7001,
            isActive = true
        });
        var category = await ReadData<ExpenseCategoryResponse>(categoryResponse);
        Assert.NotNull(category);

        var createResponse = await client.PostAsJsonAsync("/api/accounting/expenses", new
        {
            expenseCategoryId = category!.Id,
            amount = 22m,
            paymentAccountId = 1021,
            expenseDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            description = "Printer paper",
            outletId = 11
        });
        var created = await ReadData<ExpenseResponse>(createResponse);
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/expenses/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Expenses.AnyAsync(x => x.Id == created.Id));
        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "Expense"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("deleted", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        Assert.Contains(audit.Entities.SelectMany(x => x.FieldChanges), x =>
            x.FieldName == "DeletedBy" && x.NewValue?.Contains("101") == true);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Expense HTTP Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Expense User",
            Email = "expense-user@example.test",
            PasswordHash = "test",
            OutletId = 11,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = 101, OutletId = 11, BusinessId = 1, IsPrimary = true });

        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(4001, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(4011, "4010", "Sales Discount", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4021, "4020", "Sales Return", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5001, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5101, "5100", "Inventory Shortage", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5201, "5200", "Inventory Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6001, "6000", "Other Income", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(7001, "7000", "Utilities Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
        );

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1001,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1001,
            SalesAccountId = 4001,
            SalesDiscountAccountId = 4011,
            SalesReturnAccountId = 4021,
            CogsAccountId = 5001,
            InventoryShortageAccountId = 5101,
            InventoryGainAccountId = 5201,
            SupplierPayableAccountId = 2001,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 6001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3001,
            RoundingDifferenceAccountId = 6101,
            CashDifferenceGainAccountId = 6201,
            CashDifferenceLossAccountId = 6211,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 9601,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9602,
            FinancialYearId = 9601,
            PeriodNumber = 3,
            Name = "March",
            StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
        return payload?.Data;
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            IsSystemAccount = false,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            IsExpensePaymentAccount = id is 1001 or 1021,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class ExpenseCategoryResponse
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long ExpenseAccountId { get; set; }
        public string? ExpenseAccountCode { get; set; }
        public string? ExpenseAccountName { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ExpenseResponse
    {
        public long Id { get; set; }
        public string? ExpenseNumber { get; set; }
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string? ReferenceNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public long? JournalEntryId { get; set; }
    }

    private sealed class ExpenseListResponse
    {
        public List<ExpenseResponse> Expenses { get; set; } = [];
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class ExpenseSummaryResponse
    {
        public decimal TotalAmount { get; set; }
        public Dictionary<string, decimal> ByCategory { get; set; } = [];
        public int Count { get; set; }
    }
}