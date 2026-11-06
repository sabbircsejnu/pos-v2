using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class ExpenseEntryApiFactory : RetailPosApiFactory
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"RetailPosExpenseFiles_{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExpenseAttachments:StorageRoot"] = StorageRoot,
            ["ExpenseAttachments:MaxFileSizeBytes"] = "1024",
            ["ExpenseAttachments:MaxFilesPerExpense"] = "3"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(StorageRoot))
            Directory.Delete(StorageRoot, recursive: true);
    }
}

public sealed class ExpenseEntryEnhancementsHttpTests : IClassFixture<ExpenseEntryApiFactory>
{
    private const string Url = "/api/accounting/expenses";
    private const long BusinessWideUser = 101;
    private const long AssignedUser = 102;
    private const long DefaultOutletOnlyUser = 103;
    private const long OwnerUser = 104;
    private const long Step3AllUser = 105;

    private const string AllExpensePermissions =
        "accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit,accounting.expenses.submit,accounting.expenses.approve,accounting.expenses.reject,accounting.expenses.cancel,expenses.delete";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] Pdf = "%PDF-1.7 test"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];
    private static readonly byte[] WebP = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    private readonly ExpenseEntryApiFactory _factory;

    public ExpenseEntryEnhancementsHttpTests(ExpenseEntryApiFactory factory) => _factory = factory;

    // ---- Payment account lookup and eligibility ----

    [Fact]
    public async Task PaymentAccountLookup_CreateOnlyRole_GetsFlaggedActiveOwnBusinessAccountsWithoutChartAccess()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser, "accounting.expenses.create");

        var lookup = await client.GetAsync($"{Url}/payment-accounts");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var accounts = await Data<List<PaymentAccountResponse>>(lookup);
        Assert.Equal([1001L, 1021L], accounts!.Select(account => account.Id).Order());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/chart-accounts?isActive=true")).StatusCode);
    }

    [Fact]
    public async Task ExpenseFormLookups_RequireAnExpenseEntryPermission()
    {
        await SeedAsync();
        using var unrelated = Client(BusinessWideUser, "accounting.chart_accounts.view");
        using var creator = Client(BusinessWideUser, "accounting.expenses.create");

        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync($"{Url}/payment-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync($"{Url}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync($"{Url}/categories")).StatusCode);
    }

    [Theory]
    [InlineData(1500L)] // active Asset that was never flagged
    [InlineData(1900L)] // flagged but inactive
    [InlineData(9001L)] // flagged, other business
    [InlineData(7001L)] // Expense account
    public async Task CreateAndUpdate_RejectIneligiblePaymentAccount(long accountId)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Expense(accountId, outletId: 11))).StatusCode);

        var id = await CreateAsync(client, Expense(1021, outletId: 11));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{id}", Expense(accountId, outletId: 11))).StatusCode);
    }

    [Fact]
    public async Task Approve_KeepsAcceptingAnInFlightExpenseWhoseBankAccountIsNotFlagged()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.BankAccounts.Add(new BankAccount { BusinessId = 1, ChartAccountId = 1500, DisplayName = "Unflagged bank" });
            await db.SaveChangesAsync();
        }
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Submitted, outletId: 11, paymentAccountId: 1500);

        var approve = await client.PostAsJsonAsync($"{Url}/{id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
    }

    // ---- D4: payment source and account must agree ----

    [Theory]
    [InlineData("bank", 1021L)]
    [InlineData("petty_cash", 1001L)]
    [InlineData("pos_drawer", 1001L)]
    public async Task PaymentAccountLookup_BySource_ReturnsOnlyWhatThatSourceMayCredit(string source, long expected)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser, "accounting.expenses.create");

        var accounts = await Data<List<PaymentAccountResponse>>(await client.GetAsync($"{Url}/payment-accounts?paymentSource={source}&outletId=11"));

        Assert.Equal([expected], accounts!.Select(account => account.Id));
    }

    [Fact]
    public async Task PaymentAccountLookup_PosDrawerWithoutAnOutlet_IsEmpty()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser, "accounting.expenses.create");

        Assert.Empty((await Data<List<PaymentAccountResponse>>(await client.GetAsync($"{Url}/payment-accounts?paymentSource=pos_drawer&warehouseId=21")))!);
    }

    [Fact]
    public async Task PaymentAccountLookup_RefusesAnUnknownSourceAndAnUnauthorizedLocation()
    {
        await SeedAsync();
        using var client = Client(AssignedUser, "accounting.expenses.create", "Manager");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}/payment-accounts?paymentSource=cheque")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/payment-accounts?paymentSource=bank&outletId=12")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Url}/payment-accounts?paymentSource=bank&outletId=11")).StatusCode);
    }

    [Theory]
    [InlineData("bank", 1001L)]
    [InlineData("petty_cash", 1021L)]
    [InlineData("pos_drawer", 1021L)]
    [InlineData(null, 1001L)]
    public async Task CreateAndUpdate_RejectAnAccountTheSourceCannotCredit(string? source, long accountId)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Expense(accountId, outletId: 11, paymentSource: source))).StatusCode);

        var id = await CreateAsync(client, Expense(1021, outletId: 11));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{id}", Expense(accountId, outletId: 11, paymentSource: source))).StatusCode);
    }

    [Theory]
    [InlineData("bank", 1021L)]
    [InlineData("petty_cash", 1001L)]
    [InlineData("pos_drawer", 1001L)]
    public async Task Create_AcceptsAMatchingSourceAndAccount(string source, long accountId)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);

        Assert.True(await CreateAsync(client, Expense(accountId, outletId: 11, paymentSource: source)) > 0);
    }

    [Fact]
    public async Task Approve_RefusesAPendingExpenseWhoseSourceAndAccountDisagree()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Submitted, outletId: 11, paymentAccountId: 1021, paymentSource: ExpensePaymentSources.PosDrawer);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/{id}/approve", new { })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(ExpenseStatuses.Submitted, db.Expenses.Single(expense => expense.Id == id).Status);
    }

    // ---- D2/D3: category management and the expense account lookup ----

    private const string Manage = "accounting.expense_categories.manage";

    private async Task SeedLookupAccountsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        ChartAccount WithFlags(ChartAccount account, bool system = false, bool control = false, bool manual = true, long? parent = null)
        {
            account.IsSystemAccount = system;
            account.IsControlAccount = control;
            account.AllowManualPosting = manual;
            account.ParentAccountId = parent;
            return account;
        }

        db.ChartAccounts.AddRange(
            WithFlags(Account(7310, AccountingDefaults.BankFeeExpenseCode, "Bank Fee Expense", ChartAccountTypes.Expense), system: true),
            WithFlags(Account(7400, AccountingDefaults.SalesDiscountCode, "Sales Discounts", ChartAccountTypes.Expense), system: true, manual: false),
            WithFlags(Account(7402, AccountingDefaults.SalesReturnCode, "Sales Returns", ChartAccountTypes.Expense), system: true, manual: false),
            WithFlags(Account(7500, "7500", "Control", ChartAccountTypes.Expense), control: true),
            Account(7600, "7600", "Group", ChartAccountTypes.Expense),
            Account(7700, "7700", "Retired", ChartAccountTypes.Expense, active: false),
            Account(9701, "7800", "Foreign", ChartAccountTypes.Expense, businessId: 2));
        await db.SaveChangesAsync();
        db.ChartAccounts.Add(WithFlags(Account(7601, "7601", "Leaf", ChartAccountTypes.Expense), parent: 7600));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExpenseAccountLookup_ReturnsOnlyEligibleOwnBusinessAccounts_WithoutChartAccess()
    {
        await SeedAsync();
        await SeedLookupAccountsAsync();
        using var client = Client(BusinessWideUser, Manage);

        var lookup = await client.GetAsync($"{Url}/expense-accounts");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var accounts = await Data<List<AccountOptionResponse>>(lookup);

        Assert.Equal([7001L, 7310L, 7601L], accounts!.Select(account => account.Id).Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/chart-accounts")).StatusCode);
    }

    [Theory]
    [InlineData(7400L)] // Sales Discounts
    [InlineData(7402L)] // Sales Returns
    [InlineData(7500L)] // control
    [InlineData(7600L)] // parent with an active child
    [InlineData(7700L)] // inactive
    [InlineData(9701L)] // other business
    [InlineData(1021L)] // not an Expense account
    [InlineData(424242L)] // does not exist
    public async Task CategoryCreateAndRemap_RejectIneligibleAccounts(long accountId)
    {
        await SeedAsync();
        await SeedLookupAccountsAsync();
        using var client = Client(BusinessWideUser, Manage);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/categories", new { name = "New", expenseAccountId = accountId, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/categories/1", new { name = "Utilities", expenseAccountId = accountId, isActive = true })).StatusCode);
    }

    [Fact]
    public async Task Category_AcceptsTheApprovedSystemAccount_AndPreservesInactiveStatus()
    {
        await SeedAsync();
        await SeedLookupAccountsAsync();
        using var client = Client(BusinessWideUser, Manage);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Url}/categories", new { name = "Bank Charges", expenseAccountId = 7310, isActive = true })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Url}/categories/1", new { name = "Utilities", expenseAccountId = 7001, isActive = false })).StatusCode);
        var renamed = await client.PutAsJsonAsync($"{Url}/categories/1", new { name = "Utilities 2", expenseAccountId = 7001, isActive = false });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var categories = await Data<List<CategoryResponse>>(await client.GetAsync($"{Url}/categories?includeInactive=true"));
        Assert.False(categories!.Single(category => category.Id == 1).IsActive);
    }

    [Fact]
    public async Task CategoryManagement_RequiresTheDedicatedPermission_NotExpenseEditing()
    {
        await SeedAsync();
        using var expenseEditor = Client(AssignedUser, "accounting.expenses.view,accounting.expenses.create,accounting.expenses.edit", "OutletManager");
        var create = new { name = "Blocked", expenseAccountId = 7001, isActive = true };

        Assert.Equal(HttpStatusCode.Forbidden, (await expenseEditor.PostAsJsonAsync($"{Url}/categories", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expenseEditor.PutAsJsonAsync($"{Url}/categories/1", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expenseEditor.GetAsync($"{Url}/expense-accounts")).StatusCode);

        // Entering expenses keeps working.
        Assert.Equal(HttpStatusCode.OK, (await expenseEditor.GetAsync($"{Url}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await expenseEditor.GetAsync($"{Url}/payment-accounts")).StatusCode);
        Assert.True(await CreateAsync(expenseEditor, Expense(1021, outletId: 11)) > 0);
    }

    [Fact]
    public async Task CategoryManagement_WorksForACustomRoleAndTheOwnerWildcard_AndGrantsNoChartAccess()
    {
        await SeedAsync();
        using var custom = Client(AssignedUser, Manage, "CustomFinance");
        using var owner = Client(OwnerUser, "*", "BusinessOwner");

        Assert.Equal(HttpStatusCode.OK, (await custom.PostAsJsonAsync($"{Url}/categories", new { name = "Custom", expenseAccountId = 7001, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await custom.GetAsync($"{Url}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"{Url}/categories", new { name = "Owner", expenseAccountId = 7001, isActive = true })).StatusCode);

        var chartCreate = new { accountCode = "7999", accountName = "No", accountType = "Expense", normalBalanceType = "Debit", allowManualPosting = true, isActive = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await custom.PostAsJsonAsync("/api/accounting/chart-accounts", chartCreate)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await custom.PostAsync("/api/accounting/chart-accounts/7001/deactivate", null)).StatusCode);
    }

    // ---- D5 over HTTP ----

    [Fact]
    public async Task ChartAccountUpdate_CannotBypassDeactivationDependencies_AndCanReactivate()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser, "accounting.chart_accounts.view,accounting.chart_accounts.edit");
        var deactivate = new { accountName = "Utilities", allowManualPosting = true, isActive = false, displayOrder = 0 };

        var refused = await client.PutAsJsonAsync("/api/accounting/chart-accounts/7001", deactivate);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("active expense category", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/accounting/chart-accounts/7001/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task ChartAccountFlag_IsManagedOnChartAccountsOnlyForFundingAccountTypes()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser, "accounting.chart_accounts.view,accounting.chart_accounts.edit");

        var flagged = await client.PutAsJsonAsync("/api/accounting/chart-accounts/1500", ChartUpdate("Inventory", true));
        Assert.Equal(HttpStatusCode.OK, flagged.StatusCode);
        Assert.True((await Data<ChartAccountResponse>(flagged))!.IsExpensePaymentAccount);

        // A client that does not send the flag leaves it unchanged.
        var omitted = await client.PutAsJsonAsync(
            "/api/accounting/chart-accounts/1500",
            new { accountName = "Inventory", allowManualPosting = true, isActive = true, displayOrder = 0 });
        Assert.True((await Data<ChartAccountResponse>(omitted))!.IsExpensePaymentAccount);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/accounting/chart-accounts/7001", ChartUpdate("Utilities", true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/accounting/chart-accounts/2001", ChartUpdate("Supplier Payable", true))).StatusCode);
    }

    // ---- Reference numbers ----

    [Fact]
    public async Task ExpenseNumber_UsesCreationBusinessDateAndLocationPrefix_WithSeparateSequences()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var today = await BusinessTodayAsync();
        var backdated = DateTime.UtcNow.AddDays(-40).Date;

        Assert.Equal($"{today}-DHK01-0001", (await CreateDtoAsync(client, Expense(1021, outletId: 11, date: backdated))).ExpenseNumber);
        Assert.Equal($"{today}-DHK01-0002", (await CreateDtoAsync(client, Expense(1021, outletId: 11))).ExpenseNumber);
        Assert.Equal($"{today}-OUT12-0001", (await CreateDtoAsync(client, Expense(1021, outletId: 12))).ExpenseNumber);
        Assert.Equal($"{today}-WH21-0001", (await CreateDtoAsync(client, Expense(1021, warehouseId: 21))).ExpenseNumber);
        Assert.Equal($"{today}-BIZ-0001", (await CreateDtoAsync(client, Expense(1021))).ExpenseNumber);
    }

    [Fact]
    public async Task ExpenseNumber_IsImmutableOnEdit_AndExternalReferenceStaysSeparate()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var created = await CreateDtoAsync(client, Expense(1021, outletId: 11));

        var updated = await Data<ExpenseResponse>(await client.PutAsJsonAsync(
            $"{Url}/{created.Id}",
            Expense(1021, outletId: 11, amount: 77m, referenceNumber: "INV-99")));

        Assert.Equal(created.ExpenseNumber, updated!.ExpenseNumber);
        Assert.Equal("INV-99", updated.ReferenceNumber);
    }

    [Fact]
    public async Task Update_RejectsChangingLocationAfterCreation()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await CreateAsync(client, Expense(1021, outletId: 11));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{id}", Expense(1021, outletId: 12))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{id}", Expense(1021, warehouseId: 21))).StatusCode);
    }

    // ---- Location scope ----

    [Fact]
    public async Task LocationLookup_ExpenseOperation_UsesAssignmentsOrOwnerOnly()
    {
        await SeedAsync();

        using var assigned = Client(AssignedUser);
        var mine = await Data<LocationsResponse>(await assigned.GetAsync("/api/me/outlets?policy=ExpenseOperation"));
        Assert.Equal([11L], mine!.Outlets.Select(location => location.Id));
        Assert.Equal([21L], mine.Warehouses.Select(location => location.Id));
        Assert.False(mine.IsGlobalAccess);

        using var defaultOnly = Client(DefaultOutletOnlyUser);
        var none = await Data<LocationsResponse>(await defaultOnly.GetAsync("/api/me/outlets?policy=ExpenseOperation"));
        Assert.Empty(none!.Outlets);
        Assert.Empty(none.Warehouses);

        using var all = Client(BusinessWideUser);
        var everything = await Data<LocationsResponse>(await all.GetAsync("/api/me/outlets?policy=ExpenseOperation"));
        Assert.Equal([11L, 12L], everything!.Outlets.Select(location => location.Id).Order());
        Assert.Equal([21L, 22L], everything.Warehouses.Select(location => location.Id).Order());
        Assert.True(everything.IsGlobalAccess);
    }

    [Fact]
    public async Task Step3AllScope_DoesNotWidenExpenseLocationsBeyondStep5()
    {
        await SeedAsync();
        var otherOutlet = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 12);
        var businessLevel = await SeedExpenseAsync(ExpenseStatuses.Draft);
        using var client = Client(Step3AllUser);

        var locations = await Data<LocationsResponse>(await client.GetAsync("/api/me/outlets?policy=ExpenseOperation"));
        Assert.Equal([11L], locations!.Outlets.Select(location => location.Id));
        Assert.Empty(locations.Warehouses);
        Assert.False(locations.IsGlobalAccess);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Url, Expense(1021, outletId: 11))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, Expense(1021, outletId: 12))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, Expense(1021))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/{otherOutlet}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/{businessLevel}")).StatusCode);
    }

    [Fact]
    public async Task Create_EnforcesAssignmentsAndNeverTheDefaultOutlet()
    {
        await SeedAsync();
        using var assigned = Client(AssignedUser);
        using var defaultOnly = Client(DefaultOutletOnlyUser);

        Assert.Equal(HttpStatusCode.Created, (await assigned.PostAsJsonAsync(Url, Expense(1021, outletId: 11))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await assigned.PostAsJsonAsync(Url, Expense(1021, warehouseId: 21))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.PostAsJsonAsync(Url, Expense(1021, outletId: 12))).StatusCode); // their default outlet
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.PostAsJsonAsync(Url, Expense(1021, warehouseId: 22))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.PostAsJsonAsync(Url, Expense(1021))).StatusCode); // business-level
        Assert.Equal(HttpStatusCode.Forbidden, (await defaultOnly.PostAsJsonAsync(Url, Expense(1021, outletId: 11))).StatusCode);
    }

    [Fact]
    public async Task Create_AllowsOwnerEverywhere_AndRefusesForeignOrContradictoryLocations()
    {
        await SeedAsync();
        using var owner = Client(OwnerUser, role: "BusinessOwner");

        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync(Url, Expense(1021, outletId: 12))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync(Url, Expense(1021))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(Url, Expense(1021, outletId: 91))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(Url, Expense(1021, warehouseId: 92))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Url, Expense(1021, outletId: 11, warehouseId: 21))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Url, Expense(1021, warehouseId: 21, paymentSource: "pos_drawer"))).StatusCode);
    }

    [Fact]
    public async Task ReadsAndWorkflow_AreLimitedToAssignedLocations()
    {
        await SeedAsync();
        var mine = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var myWarehouse = await SeedExpenseAsync(ExpenseStatuses.Draft, warehouseId: 21);
        var otherOutlet = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 12);
        var businessLevel = await SeedExpenseAsync(ExpenseStatuses.Draft);
        var otherSubmitted = await SeedExpenseAsync(ExpenseStatuses.Submitted, outletId: 12);
        using var client = Client(AssignedUser);

        var listed = await Data<ExpenseListResponse>(await client.GetAsync($"{Url}?pageNumber=1&pageSize=50"));
        Assert.Equal(new[] { mine, myWarehouse }.Order(), listed!.Expenses.Select(expense => expense.Id).Order());
        Assert.Equal(2, listed.TotalCount);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Url}/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/{otherOutlet}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/{businessLevel}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}?outletId=12")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Url}/{otherOutlet}", Expense(1021, outletId: 12))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Url}/{otherOutlet}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Url}/{otherSubmitted}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Url}/{otherSubmitted}/reject", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Url}/{otherOutlet}/cancel", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/expenses/{otherOutlet}")).StatusCode);
    }

    [Fact]
    public async Task Summary_CountsOnlyAuthorizedLocations()
    {
        await SeedAsync();
        await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11, amount: 10m, expenseDate: DateTime.UtcNow.Date);
        await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 12, amount: 500m, expenseDate: DateTime.UtcNow.Date);
        using var client = Client(AssignedUser);

        var summary = await Data<SummaryResponse>(await client.GetAsync($"{Url}/summary"));

        Assert.Equal(10m, summary!.TotalAmount);
        Assert.Equal(1, summary.Count);
    }

    [Fact]
    public async Task OtherBusinessExpense_IsNotFound()
    {
        await SeedAsync();
        var foreign = await SeedExpenseAsync(ExpenseStatuses.Draft, businessId: 2, outletId: 91, paymentAccountId: 9001);
        using var client = Client(BusinessWideUser);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{foreign}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"{Url}/{foreign}/attachments", Upload("a.pdf", Pdf))).StatusCode);
    }

    // ---- Attachments ----

    [Theory]
    [InlineData("receipt.pdf", "application/pdf")]
    [InlineData("receipt.png", "image/png")]
    [InlineData("receipt.jpg", "image/jpeg")]
    [InlineData("receipt.jpeg", "image/jpeg")]
    [InlineData("receipt.webp", "image/webp")]
    public async Task Attachment_RoundTrips_WithServerDerivedContentTypeAndSafeHeaders(string fileName, string contentType)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var bytes = BytesFor(fileName);

        var upload = await client.PostAsync($"{Url}/{id}/attachments", Upload(fileName, bytes, "text/html"));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var attachment = await Data<AttachmentResponse>(upload);
        Assert.Equal(contentType, attachment!.ContentType);
        Assert.Equal(fileName, attachment.FileName);

        var listed = await Data<List<AttachmentResponse>>(await client.GetAsync($"{Url}/{id}/attachments"));
        Assert.Equal(attachment.Id, Assert.Single(listed!).Id);

        var download = await client.GetAsync($"{Url}/{id}/attachments/{attachment.Id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(contentType, download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(download.Headers.CacheControl!.NoStore);

        var preview = await client.GetAsync($"{Url}/{id}/attachments/{attachment.Id}/content?inline=true");
        Assert.Equal("inline", preview.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Theory]
    [InlineData("malware.pdf", "MZ-not-a-pdf")]
    [InlineData("page.png", "<html>not a png</html>")]
    [InlineData("script.exe", "%PDF-1.7 spoofed")]
    [InlineData("image.svg", "<svg onload=alert(1)>")]
    [InlineData("noextension", "%PDF-1.7")]
    public async Task Attachment_RejectsUnsupportedOrDisguisedFiles(string fileName, string content)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);

        var response = await client.PostAsync($"{Url}/{id}/attachments", Upload(fileName, System.Text.Encoding.UTF8.GetBytes(content)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Attachment_EnforcesConfiguredSizeAndCountLimits()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);

        var oversize = Pdf.Concat(new byte[1024]).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/{id}/attachments", Upload("big.pdf", oversize))).StatusCode);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Url}/{id}/attachments", Upload($"{i}.pdf", Pdf))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/{id}/attachments", Upload("over.pdf", Pdf))).StatusCode);
        Assert.Equal(3, StoredFiles().Count);
    }

    [Fact]
    public async Task Attachment_FileNameCannotEscapeStorage_AndFilesStayOutsideWwwroot()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);

        var attachment = await Data<AttachmentResponse>(await client.PostAsync($"{Url}/{id}/attachments", Upload("../../evil.pdf", Pdf)));

        Assert.Equal("evil.pdf", attachment!.FileName);
        var stored = Assert.Single(StoredFiles());
        Assert.StartsWith(_factory.StorageRoot, stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil", Path.GetFileName(stored));
        Assert.DoesNotContain("wwwroot", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Attachment_IsScopedToLocationAndPermission()
    {
        await SeedAsync();
        var other = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 12);
        using var owner = Client(BusinessWideUser);
        var attachment = await Data<AttachmentResponse>(await owner.PostAsync($"{Url}/{other}/attachments", Upload("a.pdf", Pdf)));
        using var assigned = Client(AssignedUser);
        using var noPermission = Client(BusinessWideUser, "accounting.chart_accounts.view");
        using var viewOnly = Client(BusinessWideUser, "accounting.expenses.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.GetAsync($"{Url}/{other}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.GetAsync($"{Url}/{other}/attachments/{attachment!.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.PostAsync($"{Url}/{other}/attachments", Upload("b.pdf", Pdf))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.DeleteAsync($"{Url}/{other}/attachments/{attachment.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync($"{Url}/{other}/attachments/{attachment.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.GetAsync($"{Url}/{other}/attachments/{attachment.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsync($"{Url}/{other}/attachments", Upload("c.pdf", Pdf))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.DeleteAsync($"{Url}/{other}/attachments/{attachment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Attachment_MismatchedExpenseInPath_IsNotFound()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var first = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var second = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var attachment = await Data<AttachmentResponse>(await client.PostAsync($"{Url}/{first}/attachments", Upload("a.pdf", Pdf)));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{second}/attachments/{attachment!.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Url}/{second}/attachments/{attachment.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(ExpenseStatuses.Submitted)]
    [InlineData(ExpenseStatuses.Approved)]
    [InlineData(ExpenseStatuses.Cancelled)]
    public async Task Attachment_IsReadOnlyOutsideDraftAndRejected(string status)
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var attachment = await Data<AttachmentResponse>(await client.PostAsync($"{Url}/{id}/attachments", Upload("a.pdf", Pdf)));
        await SetStatusAsync(id, status);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/{id}/attachments", Upload("b.pdf", Pdf))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"{Url}/{id}/attachments/{attachment!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Url}/{id}/attachments/{attachment.Id}/content")).StatusCode);
        Assert.Single(StoredFiles());
    }

    [Fact]
    public async Task RemovingAttachmentOrDeletingDraft_RemovesStoredFiles()
    {
        await SeedAsync();
        using var client = Client(BusinessWideUser);
        var id = await SeedExpenseAsync(ExpenseStatuses.Draft, outletId: 11);
        var first = await Data<AttachmentResponse>(await client.PostAsync($"{Url}/{id}/attachments", Upload("a.pdf", Pdf)));
        await client.PostAsync($"{Url}/{id}/attachments", Upload("b.pdf", Pdf));
        Assert.Equal(2, StoredFiles().Count);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{Url}/{id}/attachments/{first!.Id}")).StatusCode);
        Assert.Single(StoredFiles());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/expenses/{id}")).StatusCode);
        Assert.Empty(StoredFiles());
    }

    // ---- Helpers ----

    private static object Expense(
        long paymentAccountId,
        long? outletId = null,
        long? warehouseId = null,
        decimal amount = 50m,
        DateTime? date = null,
        string? referenceNumber = null,
        string? paymentSource = null) => new
        {
            expenseCategoryId = 1,
            amount,
            paymentAccountId,
            expenseDate = date ?? DateTime.UtcNow.Date,
            outletId,
            warehouseId,
            referenceNumber,
            paymentSource
        };

    private static object ChartUpdate(string name, bool flag) => new
    {
        accountName = name,
        allowManualPosting = true,
        isActive = true,
        displayOrder = 0,
        isExpensePaymentAccount = flag
    };

    private static byte[] BytesFor(string fileName) => Path.GetExtension(fileName) switch
    {
        ".pdf" => Pdf,
        ".png" => Png,
        ".webp" => WebP,
        _ => Jpg
    };

    private static MultipartFormDataContent Upload(string fileName, byte[] bytes, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private List<string> StoredFiles() => Directory.Exists(_factory.StorageRoot)
        ? Directory.GetFiles(_factory.StorageRoot, "*", SearchOption.AllDirectories).ToList()
        : [];

    private HttpClient Client(long userId, string permissions = AllExpensePermissions, string? role = null)
    {
        role ??= userId == BusinessWideUser ? "BusinessOwner" : "AccountsAdmin";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task<long> CreateAsync(HttpClient client, object body) => (await CreateDtoAsync(client, body)).Id;

    private static async Task<ExpenseResponse> CreateDtoAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Data<ExpenseResponse>(response))!;
    }

    private async Task<string> BusinessTodayAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var zone = await scope.ServiceProvider.GetRequiredService<ICompanyTimeZoneProvider>().GetTimeZoneIdAsync(1, CancellationToken.None);
        return CashSessionTime.BusinessDateFor(DateTime.UtcNow, zone).ToString("yyyyMMdd");
    }

    private async Task<long> SeedExpenseAsync(
        string status,
        long businessId = 1,
        long? outletId = null,
        long? warehouseId = null,
        long paymentAccountId = 1021,
        decimal amount = 50m,
        DateTime? expenseDate = null,
        string? paymentSource = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var expense = new Expense
        {
            BusinessId = businessId,
            Category = "Utilities",
            ExpenseCategoryId = businessId == 1 ? 1 : null,
            Amount = amount,
            TaxableAmount = amount,
            PaymentAccountId = paymentAccountId,
            PaymentSource = paymentSource,
            ExpenseDate = expenseDate ?? new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            OutletId = outletId,
            WarehouseId = warehouseId,
            Status = status,
            ExpenseNumber = $"T-{Guid.NewGuid():N}"[..20],
            CreatedBy = 999,
            SubmittedBy = status == ExpenseStatuses.Submitted ? 999 : null
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    private async Task SetStatusAsync(long id, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        (await db.Expenses.FindAsync(id))!.Status = status;
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        if (Directory.Exists(_factory.StorageRoot))
            Directory.Delete(_factory.StorageRoot, recursive: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(new Business { Id = 1, Name = "Mine" }, new Business { Id = 2, Name = "Other" });
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Dhaka", Address = "X", InvoicePrefix = "DHK01" },
            new Outlet { Id = 12, BusinessId = 1, Name = "No prefix", Address = "X" },
            new Outlet { Id = 91, BusinessId = 2, Name = "Foreign", Address = "X" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 21, BusinessId = 1, Name = "Central", Address = "X" },
            new Warehouse { Id = 22, BusinessId = 1, Name = "Annex", Address = "X" },
            new Warehouse { Id = 92, BusinessId = 2, Name = "Foreign", Address = "X" });
        db.Users.AddRange(
            NewUser(BusinessWideUser, outletId: null, User.InventoryAccessAll),
            NewUser(AssignedUser, outletId: 12, User.InventoryAccessSpecific),
            NewUser(DefaultOutletOnlyUser, outletId: 11, User.InventoryAccessAssignedOnly),
            NewUser(OwnerUser, outletId: null, User.InventoryAccessAll),
            NewUser(Step3AllUser, outletId: 12, User.InventoryAccessAll));
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = AssignedUser, OutletId = 11, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { UserId = Step3AllUser, OutletId = 11, BusinessId = 1, IsPrimary = true });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment { UserId = AssignedUser, WarehouseId = 21, BusinessId = 1 });

        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, flagged: true),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, flagged: true),
            Account(1500, "1500", "Inventory", ChartAccountTypes.Asset),
            Account(1900, "1900", "Closed bank", ChartAccountTypes.Asset, flagged: true, active: false),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability),
            Account(1300, "1300", "VAT Receivable", ChartAccountTypes.Asset),
            Account(7001, "7000", "Utilities", ChartAccountTypes.Expense),
            Account(9001, "1020", "Foreign bank", ChartAccountTypes.Asset, flagged: true, businessId: 2));
        db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, BusinessId = 1, Name = "Utilities", ExpenseAccountId = 7001, IsActive = true });

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1001,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1500,
            SalesAccountId = 7001,
            SalesDiscountAccountId = 7001,
            SalesReturnAccountId = 7001,
            CogsAccountId = 7001,
            InventoryShortageAccountId = 7001,
            InventoryGainAccountId = 7001,
            SupplierPayableAccountId = 2001,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 7001,
            OwnerCapitalAccountId = 2001,
            OwnerDrawingsAccountId = 2001,
            RoundingDifferenceAccountId = 7001,
            CashDifferenceGainAccountId = 7001,
            CashDifferenceLossAccountId = 7001,
            VatReceivableAccountId = 1300,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        db.FinancialYears.Add(new FinancialYear
        {
            Id = 9601, BusinessId = 1, YearCode = "FY26", Name = "FY26", IsCurrent = true,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9602, FinancialYearId = 9601, PeriodNumber = 3, Name = "March", Status = AccountingPeriodStatuses.Open,
            StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    }

    private static User NewUser(long id, long? outletId, string scope) => new()
    {
        Id = id,
        BusinessId = 1,
        Name = $"User {id}",
        Email = $"user{id}@example.test",
        PasswordHash = "test",
        OutletId = outletId,
        InventoryLocationAccessScope = scope,
        IsActive = true
    };

    private static ChartAccount Account(long id, string code, string name, string type, bool flagged = false, bool active = true, long businessId = 1) => new()
    {
        Id = id,
        BusinessId = businessId,
        AccountCode = code,
        AccountName = name,
        AccountType = type,
        NormalBalanceType = type == ChartAccountTypes.Asset || type == ChartAccountTypes.Expense ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit,
        AllowManualPosting = true,
        IsActive = active,
        IsExpensePaymentAccount = flagged
    };

    private static async Task<T?> Data<T>(HttpResponseMessage response) where T : class =>
        (await response.Content.ReadFromJsonAsync<Envelope<T>>(Json))?.Data;

    private sealed class Envelope<T> { public T? Data { get; set; } }
    private sealed record PaymentAccountResponse(long Id, string AccountCode, string AccountName, string AccountType);
    private sealed record AccountOptionResponse(long Id, string AccountCode, string AccountName);
    private sealed record CategoryResponse(long Id, string Name, bool IsActive);
    private sealed record ChartAccountResponse(long Id, bool IsExpensePaymentAccount);
    private sealed record ExpenseResponse(long Id, string? ExpenseNumber, string? ReferenceNumber);
    private sealed record ExpenseListResponse(List<ExpenseResponse> Expenses, int TotalCount);
    private sealed record SummaryResponse(decimal TotalAmount, int Count);
    private sealed record AttachmentResponse(long Id, string FileName, string ContentType, long FileSizeBytes);
    private sealed record LocationResponse(long Id, string Name);
    private sealed record LocationsResponse(List<LocationResponse> Outlets, List<LocationResponse> Warehouses, bool IsGlobalAccess);
}
