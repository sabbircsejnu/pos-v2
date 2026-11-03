using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class OpeningBalanceAuditTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public OpeningBalanceAuditTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpeningBalanceAndAdjustment_Posting_WritesAccountsAuditEvents()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.opening_balances.post,accounting.opening_balances.adjust");

        var openingResponse = await client.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 1500m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 1500m }
            }
        });

        Assert.True(
            openingResponse.StatusCode == HttpStatusCode.OK,
            await openingResponse.Content.ReadAsStringAsync());
        var opening = await ReadData<JournalEntryResponse>(openingResponse);
        Assert.NotNull(opening);

        var adjustResponse = await client.PostAsJsonAsync($"/api/accounting/opening-balances/{opening!.Id}/adjustments", new
        {
            reason = "Audit verification",
            adjustmentBusinessDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 60m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 60m }
            }
        });

        Assert.Equal(HttpStatusCode.OK, adjustResponse.StatusCode);
        var adjustment = await ReadData<JournalEntryResponse>(adjustResponse);
        Assert.NotNull(adjustment);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var auditRows = await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.Module == RetailPOS.Core.Entities.Audit.AuditModule.Accounts)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.NotEmpty(auditRows);
        Assert.Contains(auditRows, x => x.ActionSummary.Contains($"Journal posted: {opening.Id}", StringComparison.Ordinal));
        Assert.Contains(auditRows, x => x.ActionSummary.Contains($"Journal posted: {adjustment!.Id}", StringComparison.Ordinal));
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

        db.Businesses.Add(new Business { Id = 1, Name = "Opening Audit Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Opening Audit User",
            Email = "opening-audit@example.test",
            PasswordHash = "test",
            OutletId = 11,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1011, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1101, "1100", "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(2101, "2100", "Customer Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(3101, "3100", "Owner Drawings", ChartAccountTypes.Equity, NormalBalanceTypes.Debit),
            Account(4001, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(4011, "4010", "Sales Discount", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4021, "4020", "Sales Return", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5001, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5101, "5100", "Inventory Shortage", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5201, "5200", "Inventory Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6001, "6000", "Other Income", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
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
            Id = 9701,
            BusinessId = 1,
            YearCode = "FY2026",
            Name = "FY2026",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9702,
            FinancialYearId = 9701,
            PeriodNumber = 1,
            Name = "January",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
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
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>();
        return payload?.Data;
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class JournalEntryResponse
    {
        public long Id { get; set; }
    }
}
