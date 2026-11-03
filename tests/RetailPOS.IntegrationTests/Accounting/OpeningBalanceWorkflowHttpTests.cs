using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class OpeningBalanceWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OpeningBalanceWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostOpeningBalance_ThenAdjustment_Works_WithStatusGovernance()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.opening_balances.post,accounting.opening_balances.adjust");

        var initialStatusResponse = await client.GetAsync("/api/accounting/opening-balances/status");
        Assert.Equal(HttpStatusCode.OK, initialStatusResponse.StatusCode);
        var initialStatus = await ReadData<OpeningBalanceStatusResponse>(initialStatusResponse);
        Assert.NotNull(initialStatus);
        Assert.False(initialStatus!.HasPostedOpeningBalance);
        Assert.False(initialStatus.IsOpeningBalanceLocked);
        Assert.True(initialStatus.CanPostOpeningBalance);

        var postResponse = await client.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            description = "Initial opening balance",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 2000m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 2000m }
            }
        });

        Assert.True(
            postResponse.StatusCode == HttpStatusCode.OK,
            await postResponse.Content.ReadAsStringAsync());
        var openingPosted = await ReadData<JournalEntryResponse>(postResponse);
        Assert.NotNull(openingPosted);
        Assert.Equal("Posted", openingPosted!.Status);
        Assert.Equal("OpeningBalance", openingPosted.JournalType);

        var statusResponse = await client.GetAsync("/api/accounting/opening-balances/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await ReadData<OpeningBalanceStatusResponse>(statusResponse);
        Assert.NotNull(status);
        Assert.True(status!.HasPostedOpeningBalance);
        Assert.Equal(openingPosted.Id, status.OpeningBalanceJournalEntryId);

        var adjustmentResponse = await client.PostAsJsonAsync($"/api/accounting/opening-balances/{openingPosted.Id}/adjustments", new
        {
            reason = "Correct payable carry-forward",
            adjustmentBusinessDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            referenceNumber = "OB-ADJ-01",
            description = "Payable correction",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 100m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 100m }
            }
        });

        Assert.Equal(HttpStatusCode.OK, adjustmentResponse.StatusCode);
        var adjustmentPayload = await adjustmentResponse.Content
            .ReadFromJsonAsync<ApiEnvelope<JournalEntryResponse>>(JsonOptions);
        Assert.Equal(
            "Opening balance adjustment posted successfully",
            adjustmentPayload?.Message);
        var adjustmentPosted = adjustmentPayload?.Data;
        Assert.NotNull(adjustmentPosted);
        Assert.Equal("Posted", adjustmentPosted!.Status);
        Assert.Equal("OpeningBalanceAdjustment", adjustmentPosted.JournalType);
        Assert.Equal("OpeningBalanceAdjustment", adjustmentPosted.ReferenceType);
        Assert.StartsWith($"{openingPosted.Id}:", adjustmentPosted.ReferenceId, StringComparison.Ordinal);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var adjustment = await db.JournalEntries
                .Include(journal => journal.Lines)
                .SingleAsync(journal => journal.Id == adjustmentPosted.Id);
            Assert.Equal(1, adjustment.BusinessId);
            Assert.Equal(101L, adjustment.PostedBy);
            Assert.Equal(new DateTime(2026, 1, 15), adjustment.BusinessDate);
            Assert.Equal(new DateTime(2026, 1, 15), adjustment.PostingDate);
            Assert.Equal("OB-ADJ-01", adjustment.ReferenceNumber);
            Assert.Equal(
                "Payable correction (Reason: Correct payable carry-forward)",
                adjustment.Description);
            Assert.Equal(2, adjustment.Lines.Count);
            Assert.Equal(100m, adjustment.Lines.Single(line => line.AccountId == 1001).DebitAmount);
            Assert.Equal(100m, adjustment.Lines.Single(line => line.AccountId == 2001).CreditAmount);
        }
    }

    [Fact]
    public async Task PostOpeningBalance_Rejects_WhenRequiredBalanceSheetMappingMissing()
    {
        await ResetAndSeedAsync(missingSupplierPayableMapping: true);
        using var client = CreateClient("accounting.opening_balances.post");

        var response = await client.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 1200m, creditAmount = 0m },
                new { accountId = 3001L, debitAmount = 0m, creditAmount = 1200m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await ReadMessageAsync(response);
        Assert.Contains("supplier_payable", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostOpeningBalance_Rejects_WhenOperationalJournalAlreadyPosted()
    {
        await ResetAndSeedAsync(seedOperationalPostedJournal: true);
        using var client = CreateClient("accounting.opening_balances.post");

        var response = await client.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 1200m, creditAmount = 0m },
                new { accountId = 3001L, debitAmount = 0m, creditAmount = 1200m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await ReadMessageAsync(response);
        Assert.Contains("locked", message, StringComparison.OrdinalIgnoreCase);

        var statusResponse = await client.GetAsync("/api/accounting/opening-balances/status");
        var status = await ReadData<OpeningBalanceStatusResponse>(statusResponse);
        Assert.NotNull(status);
        Assert.True(status!.IsOpeningBalanceLocked);
        Assert.Equal(9901L, status.LockingJournalEntryId);
        Assert.Equal(AccountingJournalTypes.ManualJournal, status.LockingJournalType);
        Assert.False(status.CanPostOpeningBalance);
    }

    [Fact]
    public async Task PostAdjustment_Requires_DedicatedPermission()
    {
        await ResetAndSeedAsync();
        using var postOnlyClient = CreateClient("accounting.opening_balances.post");

        var openingResponse = await postOnlyClient.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 800m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 800m }
            }
        });

        var opening = await ReadData<JournalEntryResponse>(openingResponse);
        Assert.NotNull(opening);

        var adjustResponse = await postOnlyClient.PostAsJsonAsync($"/api/accounting/opening-balances/{opening!.Id}/adjustments", new
        {
            reason = "Permission check",
            adjustmentBusinessDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 25m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 25m }
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, adjustResponse.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_Rejects_WhenDateFallsInLockedPeriod()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.opening_balances.post,accounting.opening_balances.adjust");

        var openingResponse = await client.PostAsJsonAsync("/api/accounting/opening-balances", new
        {
            accountingStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 700m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 700m }
            }
        });

        var opening = await ReadData<JournalEntryResponse>(openingResponse);
        Assert.NotNull(opening);

        await SeedLockedPeriodAsync();

        var adjustResponse = await client.PostAsJsonAsync($"/api/accounting/opening-balances/{opening!.Id}/adjustments", new
        {
            reason = "Late correction",
            adjustmentBusinessDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 25m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 25m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, adjustResponse.StatusCode);
        var message = await ReadMessageAsync(adjustResponse);
        Assert.Contains("closed or locked", message, StringComparison.OrdinalIgnoreCase);
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

    private async Task ResetAndSeedAsync(bool missingSupplierPayableMapping = false, bool seedOperationalPostedJournal = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Opening Test Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Opening Test User",
            Email = "opening-user@example.test",
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
            SupplierPayableAccountId = missingSupplierPayableMapping ? null : 2001,
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

        db.AccountingPeriods.AddRange(
            new AccountingPeriod
            {
                Id = 9602,
                FinancialYearId = 9601,
                PeriodNumber = 1,
                Name = "Jan",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new AccountingPeriod
            {
                Id = 9603,
                FinancialYearId = 9601,
                PeriodNumber = 2,
                Name = "Feb",
                StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        if (seedOperationalPostedJournal)
        {
            db.JournalEntries.Add(new JournalEntry
            {
                Id = 9901,
                BusinessId = 1,
                JournalType = AccountingJournalTypes.ManualJournal,
                PostingDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                TransactionDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                ReferenceType = "Manual",
                ReferenceId = "LOCK-1",
                Status = JournalEntryStatuses.Posted,
                JournalNumber = "JR-2026-999001",
                PostedAt = DateTime.UtcNow,
                PostedBy = 101,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Lines =
                [
                    new JournalEntryLine { AccountId = 1001, DebitAmount = 50, CreditAmount = 0, CreatedAt = DateTime.UtcNow },
                    new JournalEntryLine { AccountId = 2001, DebitAmount = 0, CreditAmount = 50, CreatedAt = DateTime.UtcNow }
                ]
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedLockedPeriodAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var febPeriod = await db.AccountingPeriods.FirstAsync(x => x.Id == 9603);
        febPeriod.Status = AccountingPeriodStatuses.Locked;
        febPeriod.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
        return payload?.Data;
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions);
        return payload?.Message ?? string.Empty;
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

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class JournalEntryResponse
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string JournalType { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
    }

    private sealed class OpeningBalanceStatusResponse
    {
        public bool HasPostedOpeningBalance { get; set; }
        public long? OpeningBalanceJournalEntryId { get; set; }
        public bool IsOpeningBalanceLocked { get; set; }
        public long? LockingJournalEntryId { get; set; }
        public string? LockingJournalType { get; set; }
        public bool CanPostOpeningBalance { get; set; }
    }
}
