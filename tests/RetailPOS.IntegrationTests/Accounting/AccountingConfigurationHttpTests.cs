using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class AccountingConfigurationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AccountingConfigurationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Configuration_Endpoints_Enforce_Canonical_Permissions()
    {
        await ResetAndSeedAsync();

        using var noView = CreateClient("accounting.configuration.edit");
        using var noEdit = CreateClient("accounting.configuration.view");

        var getResponse = await noView.GetAsync("/api/accounting/configuration");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var putResponse = await noEdit.PutAsJsonAsync("/api/accounting/configuration", new
        {
            cashAccountId = 1001L,
            pettyCashAccountId = 1011L,
            defaultBankAccountId = 1021L,
            inventoryAccountId = 1101L,
            salesAccountId = 4001L,
            salesDiscountAccountId = 4011L,
            salesReturnAccountId = 4021L,
            cogsAccountId = 5001L,
            inventoryShortageAccountId = 5101L,
            inventoryGainAccountId = 5201L,
            supplierPayableAccountId = 2001L,
            customerAdvanceAccountId = 2101L,
            otherIncomeAccountId = 6001L,
            ownerCapitalAccountId = 3001L,
            ownerDrawingsAccountId = 3101L,
            retainedEarningsAccountId = 3201L,
            roundingDifferenceAccountId = 6101L,
            cashDifferenceGainAccountId = 6201L,
            cashDifferenceLossAccountId = 6211L,
            journalNumberPrefix = "JR",
            journalNumberPadding = 6,
            journalSequenceResetPolicy = "PerFinancialYear"
        });

        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }

    [Fact]
    public async Task Outlet_Configuration_Endpoints_Enforce_Outlet_Authorization()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.view,accounting.configuration.edit");

        var unauthorizedGet = await client.GetAsync("/api/accounting/configuration/outlets/22");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedGet.StatusCode);

        var unauthorizedPut = await client.PutAsJsonAsync("/api/accounting/configuration/outlets/22", new
        {
            outletId = 22L,
            outletCashAccountId = 1001L,
            outletPettyCashAccountId = 1011L,
            outletBankAccountId = 1021L
        });

        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedPut.StatusCode);
    }

    [Fact]
    public async Task OutletConfigurationUpsertRejectsAccountTypeMismatchWithoutCreatingRow()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.edit");

        using var response = await client.PutAsJsonAsync(
            "/api/accounting/configuration/outlets/11",
            new
            {
                outletId = 11L,
                outletCashAccountId = 4001L,
                outletPettyCashAccountId = 1011L,
                outletBankAccountId = 1021L
            });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errorDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Mapped account 4001 for slot 'cash' must be one of: Asset.",
            errorDocument.RootElement.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.AccountingOutletConfigurations.ToListAsync());
    }

    [Fact]
    public async Task OutletConfigurationReads_ReturnBusinessRowsInOutletOrderAndProjectSingleRow()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.AccountingOutletConfigurations.AddRange(
                new AccountingOutletConfiguration
                {
                    BusinessId = 1,
                    OutletId = 22,
                    OutletCashAccountId = 1099
                },
                new AccountingOutletConfiguration
                {
                    BusinessId = 1,
                    OutletId = 11,
                    OutletCashAccountId = 1001
                },
                new AccountingOutletConfiguration
                {
                    BusinessId = 2,
                    OutletId = 99,
                    OutletCashAccountId = 1021
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient("accounting.configuration.view");
        using var listResponse = await client.GetAsync("/api/accounting/configuration/outlets");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var rows = await ReadData<List<AccountingOutletConfigurationResponse>>(listResponse);
        Assert.NotNull(rows);
        Assert.Equal(new long[] { 11, 22 }, rows!.Select(row => row.OutletId));
        Assert.Equal(1001L, rows[0].OutletCashAccountId);

        using var singleResponse = await client.GetAsync("/api/accounting/configuration/outlets/11");
        Assert.Equal(HttpStatusCode.OK, singleResponse.StatusCode);
        var single = await ReadData<AccountingOutletConfigurationResponse>(singleResponse);
        Assert.NotNull(single);
        Assert.Equal(11L, single!.OutletId);
        Assert.Equal(1001L, single.OutletCashAccountId);
    }

    [Fact]
    public async Task Resolve_Uses_Outlet_Override_Then_Global_Fallback()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.view,accounting.configuration.edit");

        var resolveGlobalCash = await client.GetAsync(
            "/api/accounting/configuration/resolve?slot=cash&outletId=11");
        Assert.Equal(HttpStatusCode.OK, resolveGlobalCash.StatusCode);
        var globalCashData = await ReadData<PostingAccountResolutionResponse>(resolveGlobalCash);
        Assert.NotNull(globalCashData);
        Assert.Equal(1001L, globalCashData!.AccountId);
        Assert.Equal("GlobalConfiguration", globalCashData.Source);

        var upsertResponse = await client.PutAsJsonAsync("/api/accounting/configuration/outlets/11", new
        {
            outletId = 11L,
            outletCashAccountId = 1099L,
            outletPettyCashAccountId = 1011L,
            outletBankAccountId = 1021L
        });

        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var resolveCash = await client.GetAsync("/api/accounting/configuration/resolve?slot=cash&outletId=11");
        Assert.Equal(HttpStatusCode.OK, resolveCash.StatusCode);
        var cashData = await ReadData<PostingAccountResolutionResponse>(resolveCash);
        Assert.NotNull(cashData);
        Assert.Equal(1099L, cashData!.AccountId);
        Assert.Equal("OutletOverride", cashData.Source);

        var resolveInventory = await client.GetAsync("/api/accounting/configuration/resolve?slot=inventory&outletId=11");
        Assert.Equal(HttpStatusCode.OK, resolveInventory.StatusCode);
        var inventoryData = await ReadData<PostingAccountResolutionResponse>(resolveInventory);
        Assert.NotNull(inventoryData);
        Assert.Equal(1101L, inventoryData!.AccountId);
        Assert.Equal("GlobalConfiguration", inventoryData.Source);
    }

    [Fact]
    public async Task Resolve_NormalizesSlotAndPreservesUnsupportedSlotError()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.view");

        using var normalizedResponse = await client.GetAsync(
            "/api/accounting/configuration/resolve?slot=%20CASH%20");
        Assert.Equal(HttpStatusCode.OK, normalizedResponse.StatusCode);
        var normalized = await ReadData<PostingAccountResolutionResponse>(normalizedResponse);
        Assert.NotNull(normalized);
        Assert.Equal("cash", normalized!.Slot);
        Assert.Equal(1001L, normalized.AccountId);

        using var unsupportedResponse = await client.GetAsync(
            "/api/accounting/configuration/resolve?slot=not_a_posting_slot");
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
        var error = await unsupportedResponse.Content.ReadAsStringAsync();
        using var errorDocument = JsonDocument.Parse(error);
        Assert.Equal(
            "Unsupported posting account slot 'not_a_posting_slot'.",
            errorDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Configuration_Can_Update_ManualJournal_Workflow_Settings()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.view,accounting.configuration.edit");

        var putResponse = await client.PutAsJsonAsync("/api/accounting/configuration", new
        {
            cashAccountId = 1001L,
            pettyCashAccountId = 1011L,
            defaultBankAccountId = 1021L,
            inventoryAccountId = 1101L,
            salesAccountId = 4001L,
            salesDiscountAccountId = 4011L,
            salesReturnAccountId = 4021L,
            cogsAccountId = 5001L,
            inventoryShortageAccountId = 5101L,
            inventoryGainAccountId = 5201L,
            supplierPayableAccountId = 2001L,
            customerAdvanceAccountId = 2101L,
            otherIncomeAccountId = 6001L,
            ownerCapitalAccountId = 3001L,
            ownerDrawingsAccountId = 3101L,
            retainedEarningsAccountId = 3201L,
            roundingDifferenceAccountId = 6101L,
            cashDifferenceGainAccountId = 6201L,
            cashDifferenceLossAccountId = 6211L,
            journalNumberPrefix = "JR",
            journalNumberPadding = 6,
            journalSequenceResetPolicy = "PerFinancialYear",
            defaultSupplierPaymentTermsDays = 45,
            manualJournalWorkflowMode = "ApprovalRequired",
            manualJournalAllowSelfApproval = true,
            manualJournalAttachmentMaxBytes = 20971520L
        });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/accounting/configuration");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var config = await ReadData<AccountingConfigurationResponse>(getResponse);
        Assert.NotNull(config);
        Assert.Equal("ApprovalRequired", config!.ManualJournalWorkflowMode);
        Assert.True(config.ManualJournalAllowSelfApproval);
        Assert.Equal(20971520L, config.ManualJournalAttachmentMaxBytes);
        Assert.Equal(3201L, config.RetainedEarningsAccountId);
        Assert.Equal(45, config.DefaultSupplierPaymentTermsDays);
    }

    [Fact]
    public async Task Configuration_UpdateRejectsAccountTypeMismatchWithoutChangingExistingMapping()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.configuration.edit");

        using var response = await client.PutAsJsonAsync(
            "/api/accounting/configuration",
            new { salesAccountId = 1101L });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errorDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Mapped account 1101 for slot 'sales' must be one of: Income.",
            errorDocument.RootElement.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var configuration = await db.AccountingConfigurations.SingleAsync();
        Assert.Equal(4001L, configuration.SalesAccountId);
    }

    [Fact]
    public async Task GetBusinessConfiguration_InitializesMissingAccountsConfigurationAndFinancialYear()
    {
        await ResetToUninitializedBusinessAsync();
        using var client = CreateClient("accounting.configuration.view");

        using var response = await client.GetAsync("/api/accounting/configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = await ReadData<AccountingConfigurationResponse>(response);
        Assert.NotNull(configuration);
        Assert.True(configuration!.CashAccountId > 0);
        Assert.True(configuration.InventoryAccountId > 0);
        Assert.True(configuration.RetainedEarningsAccountId > 0);
        Assert.Equal("JR", configuration.JournalNumberPrefix);
        Assert.Equal(6, configuration.JournalNumberPadding);
        Assert.Equal(30, configuration.DefaultSupplierPaymentTermsDays);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(AccountingDefaults.ChartOfAccounts.Count,
            await db.ChartAccounts.CountAsync(account => account.BusinessId == 1));
        var persistedConfiguration = await db.AccountingConfigurations.SingleAsync();
        Assert.Null(persistedConfiguration.CreatedBy);
        Assert.NotNull(persistedConfiguration.CashAccountId);
        var year = await db.FinancialYears.SingleAsync();
        Assert.Equal(1, await db.AccountingPeriods.CountAsync(period => period.FinancialYearId == year.Id));
        Assert.Null(year.CreatedBy);
    }

    [Fact]
    public async Task Resolve_InitializesMissingBusinessConfiguration()
    {
        await ResetToUninitializedBusinessAsync();
        using var client = CreateClient("accounting.configuration.view");

        using var response = await client.GetAsync("/api/accounting/configuration/resolve?slot=cash");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolution = await ReadData<PostingAccountResolutionResponse>(response);
        Assert.NotNull(resolution);
        Assert.Equal("cash", resolution!.Slot);
        Assert.True(resolution.AccountId > 0);
        Assert.Equal("GlobalConfiguration", resolution.Source);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.AccountingConfigurations.ToListAsync());
        Assert.Single(await db.FinancialYears.ToListAsync());
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

        db.Businesses.Add(new Business { Id = 1, Name = "Config Test Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Outlet { Id = 22, BusinessId = 1, Name = "Second Outlet", Address = "Y", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Config Scoped User",
                Email = "config-scoped@example.test",
                PasswordHash = "test",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = 102,
                BusinessId = 1,
                Name = "Config Other Outlet User",
                Email = "config-other@example.test",
                PasswordHash = "test",
                OutletId = 22,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1011, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1099, "1099", "Outlet Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1101, "1100", "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(2101, "2100", "Customer Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(3101, "3100", "Owner Drawings", ChartAccountTypes.Equity, NormalBalanceTypes.Debit),
            Account(3201, "3200", "Retained Earnings", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(4001, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(4011, "4010", "Sales Discount", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4021, "4020", "Sales Return", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5001, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5101, "5100", "Inventory Shortage", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5201, "5200", "Inventory Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6001, "6000", "Other Income", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Diff Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Diff Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
        );

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1011,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1101,
            SalesAccountId = 4001,
            SalesDiscountAccountId = 4011,
            SalesReturnAccountId = 4021,
            CogsAccountId = 5001,
            InventoryShortageAccountId = 5101,
            InventoryGainAccountId = 5201,
            SupplierPayableAccountId = 2001,
            CustomerAdvanceAccountId = 2101,
            OtherIncomeAccountId = 6001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3101,
            RetainedEarningsAccountId = 3201,
            RoundingDifferenceAccountId = 6101,
            CashDifferenceGainAccountId = 6201,
            CashDifferenceLossAccountId = 6211,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task ResetToUninitializedBusinessAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Uninitialized Config Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
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
            IsSystemAccount = true,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiResponseEnvelope<T>>(JsonOptions);
        return payload?.Data;
    }

    private sealed class ApiResponseEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; } = default!;
    }

    private sealed class PostingAccountResolutionResponse
    {
        public string Slot { get; set; } = string.Empty;
        public long AccountId { get; set; }
        public string Source { get; set; } = string.Empty;
        public long? OutletId { get; set; }
    }

    private sealed class AccountingOutletConfigurationResponse
    {
        public long OutletId { get; set; }
        public long? OutletCashAccountId { get; set; }
        public long? OutletPettyCashAccountId { get; set; }
        public long? OutletBankAccountId { get; set; }
    }

    private sealed class AccountingConfigurationResponse
    {
        public long? CashAccountId { get; set; }
        public long? InventoryAccountId { get; set; }
        public long? RetainedEarningsAccountId { get; set; }
        public string JournalNumberPrefix { get; set; } = string.Empty;
        public int JournalNumberPadding { get; set; }
        public int DefaultSupplierPaymentTermsDays { get; set; }
        public string ManualJournalWorkflowMode { get; set; } = string.Empty;
        public bool ManualJournalAllowSelfApproval { get; set; }
        public long ManualJournalAttachmentMaxBytes { get; set; }
    }
}
