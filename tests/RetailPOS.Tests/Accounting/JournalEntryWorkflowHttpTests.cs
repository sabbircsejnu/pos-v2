using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class JournalEntryWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JournalEntryWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Post_Reverse_Lifecycle_Works()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.journals.view,accounting.journals.create,accounting.journals.post,accounting.journals.reverse");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            referenceType = "Manual",
            referenceId = "HTTP-1",
            description = "Manual journal via API",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 120m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 120m }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);
        Assert.Equal("Draft", created!.Status);
        Assert.True(string.IsNullOrWhiteSpace(created.JournalNumber));

        var postResponse = await client.PostAsync($"/api/accounting/journals/{created.Id}/post", null);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var posted = await ReadData<JournalEntryResponse>(postResponse);
        Assert.NotNull(posted);
        Assert.Equal("Posted", posted!.Status);
        Assert.Equal("JR-2026-000001", posted.JournalNumber);

        var reverseResponse = await client.PostAsJsonAsync($"/api/accounting/journals/{created.Id}/reverse", new { reason = "Correction" });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);
        var reversal = await ReadData<JournalEntryResponse>(reverseResponse);
        Assert.NotNull(reversal);
        Assert.Equal("Posted", reversal!.Status);

        var originalGet = await client.GetAsync($"/api/accounting/journals/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, originalGet.StatusCode);
        var original = await ReadData<JournalEntryResponse>(originalGet);
        Assert.NotNull(original);
        Assert.Equal("Reversed", original!.Status);
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.journals.view");

        var response = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = DateTime.UtcNow,
            postingDate = DateTime.UtcNow,
            referenceType = "Manual",
            referenceId = "HTTP-2",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 10m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 10m }
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_LockedPeriod_ReturnsBadRequest()
    {
        await ResetAndSeedAsync(lockPeriod: true);
        using var client = CreateClient("accounting.journals.view,accounting.journals.create,accounting.journals.post");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            referenceType = "Manual",
            referenceId = "HTTP-LCK",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 10m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 10m }
            }
        });

        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var postResponse = await client.PostAsync($"/api/accounting/journals/{created!.Id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
    }

    [Fact]
    public async Task Update_And_Cancel_Draft_Lifecycle_Works()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.journals.view,accounting.journals.create,accounting.journals.update,accounting.journals.cancel");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
            referenceType = "Manual",
            referenceId = "HTTP-UPD-1",
            description = "Draft before update",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 80m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 80m }
            }
        });

        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync($"/api/accounting/journals/{created!.Id}", new
        {
            journalType = "ManualJournal",
            transactionDate = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
            businessDate = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
            referenceType = "Manual",
            referenceId = "HTTP-UPD-1",
            referenceNumber = "REF-HTTP-UPD-1",
            description = "Draft after update",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 95m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 95m }
            }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadData<JournalEntryResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal("Draft", updated!.Status);
        Assert.Equal("Draft after update", updated.Description);
        Assert.Equal(11, updated.OutletId);

        var cancelResponse = await client.PostAsJsonAsync($"/api/accounting/journals/{created.Id}/cancel", new
        {
            reason = "Cancelled by test"
        });

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await ReadData<JournalEntryResponse>(cancelResponse);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task Journal_Apis_Enforce_Outlet_Authorization_For_Read_And_Write()
    {
        await ResetAndSeedAsync();
        await SeedExternalOutletDraftAsync();

        using var client = CreateClient("accounting.journals.view,accounting.journals.create,accounting.journals.update,accounting.journals.cancel,accounting.journals.post,accounting.journals.reverse");

        var unauthorizedSearch = await client.GetAsync("/api/accounting/journals?outletId=22");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedSearch.StatusCode);

        var unauthorizedGet = await client.GetAsync("/api/accounting/journals/7001");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedGet.StatusCode);

        var unauthorizedCreate = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = DateTime.UtcNow,
            postingDate = DateTime.UtcNow,
            outletId = 22L,
            referenceType = "Manual",
            referenceId = "HTTP-OUTLET-CREATE",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 10m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 10m }
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedCreate.StatusCode);

        var unauthorizedUpdate = await client.PutAsJsonAsync("/api/accounting/journals/7001", new
        {
            journalType = "ManualJournal",
            transactionDate = DateTime.UtcNow,
            postingDate = DateTime.UtcNow,
            businessDate = DateTime.UtcNow,
            outletId = 22L,
            referenceType = "Manual",
            referenceId = "HTTP-OUTLET-UPD",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 25m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 25m }
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedUpdate.StatusCode);

        var unauthorizedCancel = await client.PostAsJsonAsync("/api/accounting/journals/7001/cancel", new { reason = "No access" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedCancel.StatusCode);

        var unauthorizedPost = await client.PostAsync("/api/accounting/journals/7001/post", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedPost.StatusCode);

        await PostJournalDirectAsync(7001, 22, "JR-2026-009901");
        var unauthorizedReverse = await client.PostAsJsonAsync("/api/accounting/journals/7001/reverse", new { reason = "No access" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedReverse.StatusCode);
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

    private async Task ResetAndSeedAsync(bool lockPeriod = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Test Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 22, BusinessId = 1, Name = "Second Outlet", Address = "Y", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Journal Scoped User",
                Email = "journal-scoped@example.test",
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
                Name = "Journal Other Outlet User",
                Email = "journal-other@example.test",
                PasswordHash = "test",
                OutletId = 22,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        db.ChartAccounts.AddRange(
            new ChartAccount
            {
                Id = 1001,
                BusinessId = 1,
                AccountCode = "1000",
                AccountName = "Cash",
                AccountType = ChartAccountTypes.Asset,
                NormalBalanceType = NormalBalanceTypes.Debit,
                IsSystemAccount = true,
                AllowManualPosting = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ChartAccount
            {
                Id = 2001,
                BusinessId = 1,
                AccountCode = "2000",
                AccountName = "Payable",
                AccountType = ChartAccountTypes.Liability,
                NormalBalanceType = NormalBalanceTypes.Credit,
                IsSystemAccount = true,
                AllowManualPosting = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        if (lockPeriod)
        {
            db.FinancialYears.Add(new FinancialYear
            {
                Id = 3001,
                BusinessId = 1,
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
                Id = 3101,
                FinancialYearId = 3001,
                PeriodNumber = 7,
                Name = "July",
                StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Locked,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settingsService.UpdateInventorySettingsAsync(new RetailPOS.API.Settings.InventorySettings
        {
            LowStockThreshold = 10,
            EnableLowStockAlerts = true,
            AllowNegativeStock = false,
            AutoReorder = false
        });
    }

    private async Task SeedExternalOutletDraftAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var now = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 7001,
            BusinessId = 1,
            JournalType = "ManualJournal",
            TransactionDate = now,
            PostingDate = now,
            BusinessDate = now.Date,
            OutletId = 22,
            ReferenceType = "Manual",
            ReferenceId = "HTTP-OUTLET-7001",
            Description = "Other outlet draft",
            Status = JournalEntryStatuses.Draft,
            IsSystemGenerated = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 102,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = 102,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = 1001,
                    DebitAmount = 20,
                    CreditAmount = 0,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = 102
                },
                new JournalEntryLine
                {
                    AccountId = 2001,
                    DebitAmount = 0,
                    CreditAmount = 20,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = 102
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    private async Task PostJournalDirectAsync(long journalId, long outletId, string journalNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var now = DateTime.UtcNow;

        var journal = await db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == journalId && x.BusinessId == 1);
        journal.OutletId = outletId;
        journal.Status = JournalEntryStatuses.Posted;
        journal.JournalNumber = journalNumber;
        journal.PostedAt = now;
        journal.PostedBy = 102;
        journal.UpdatedAt = now;
        journal.UpdatedBy = 102;

        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class JournalEntryResponse
    {
        public long Id { get; set; }
        public string? JournalNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long? OutletId { get; set; }
    }
}
