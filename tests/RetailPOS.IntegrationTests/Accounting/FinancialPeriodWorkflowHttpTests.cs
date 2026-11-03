using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Commands;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class FinancialPeriodWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FinancialPeriodWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FinancialYear_And_Period_Endpoints_EnforceCanonicalPermissions()
    {
        await ResetAndSeedAsync();

        using var noYearView = CreateClient("accounting.financial_years.manage");
        using var noYearManage = CreateClient("accounting.financial_years.view");
        using var noPeriodClose = CreateClient("accounting.financial_years.view,accounting.financial_years.manage,accounting.periods.view");

        var getYears = await noYearView.GetAsync("/api/accounting/financial-years");
        Assert.Equal(HttpStatusCode.Forbidden, getYears.StatusCode);

        var createYear = await noYearManage.PostAsJsonAsync("/api/accounting/financial-years", new
        {
            yearCode = "FY2027",
            name = "FY 2027",
            startDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            isCurrent = false
        });
        Assert.Equal(HttpStatusCode.Forbidden, createYear.StatusCode);

        var lockPeriod = await noPeriodClose.PostAsJsonAsync("/api/accounting/periods/3101/lock", new { reason = "freeze" });
        Assert.Equal(HttpStatusCode.Forbidden, lockPeriod.StatusCode);
    }

    [Fact]
    public async Task PeriodStatus_Transitions_AreDeterministic_AndReasonGoverned()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.financial_years.view,accounting.financial_years.manage,accounting.periods.view,accounting.periods.close,accounting.periods.reopen");

        var missingLockReason = await client.PostAsJsonAsync("/api/accounting/periods/3101/lock", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingLockReason.StatusCode);

        var lockResponse = await client.PostAsJsonAsync("/api/accounting/periods/3101/lock", new { reason = "Month-end freeze" });
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var repeatedLock = await client.PostAsJsonAsync("/api/accounting/periods/3101/lock", new { reason = "Month-end freeze" });
        Assert.Equal(HttpStatusCode.OK, repeatedLock.StatusCode);

        var closeLocked = await client.PostAsJsonAsync("/api/accounting/periods/3101/close", new { reason = "Trying invalid transition" });
        Assert.Equal(HttpStatusCode.BadRequest, closeLocked.StatusCode);

        var reopenMissingReason = await client.PostAsJsonAsync("/api/accounting/periods/3101/reopen", new { });
        Assert.Equal(HttpStatusCode.BadRequest, reopenMissingReason.StatusCode);

        var reopen = await client.PostAsJsonAsync("/api/accounting/periods/3101/reopen", new { reason = "Approved by finance manager" });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
    }

    [Fact]
    public async Task PeriodCommands_PreserveValidationBusinessScopeMappingAndAuditBehavior()
    {
        await ResetAndSeedAsync(includeSecondBusiness: true);
        using var client = CreateClient("accounting.financial_years.manage");

        var create = await client.PostAsJsonAsync("/api/accounting/financial-years/3001/periods", new
        {
            periodNumber = 3,
            name = " March ",
            startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            status = "open"
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await ReadData<AccountingPeriodCommandResponse>(create);
        Assert.NotNull(created);
        Assert.Equal(3, created!.PeriodNumber);
        Assert.Equal("March", created.Name);
        Assert.Equal(AccountingPeriodStatuses.Open, created.Status);

        var update = await client.PutAsJsonAsync($"/api/accounting/periods/{created.Id}", new
        {
            periodNumber = 4,
            name = " March Updated ",
            startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadData<AccountingPeriodCommandResponse>(update);
        Assert.NotNull(updated);
        Assert.Equal(4, updated!.PeriodNumber);
        Assert.Equal("March Updated", updated.Name);
        Assert.Equal(AccountingPeriodStatuses.Open, updated.Status);

        var overlap = await client.PostAsJsonAsync("/api/accounting/financial-years/3001/periods", new
        {
            periodNumber = 4,
            name = "Overlapping February",
            startDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            status = "open"
        });
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        var invalidNumber = await client.PostAsJsonAsync("/api/accounting/financial-years/3001/periods", new
        {
            periodNumber = 0,
            name = "Invalid number",
            startDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            status = "open"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNumber.StatusCode);

        var crossBusiness = await client.PostAsJsonAsync("/api/accounting/financial-years/9001/periods", new
        {
            periodNumber = 1,
            name = "Cross Business",
            startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            status = "open"
        });
        Assert.Equal(HttpStatusCode.NotFound, crossBusiness.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var period = await db.AccountingPeriods.AsNoTracking()
            .SingleAsync(item => item.Id == created.Id);
        Assert.Equal(101L, period.CreatedBy);
        Assert.Equal(101L, period.UpdatedBy);
        Assert.DoesNotContain(await db.AccountingPeriods.AsNoTracking().ToListAsync(),
            item => item.Name == "Overlapping February" || item.Name == "Invalid number" || item.Name == "Cross Business");

        var auditSummaries = await db.AuditEvents
            .AsNoTracking()
            .Select(auditEvent => auditEvent.ActionSummary)
            .ToListAsync();
        Assert.Contains(auditSummaries, summary => summary.Contains($"Accounting period created: {created.Id}", StringComparison.Ordinal));
        Assert.Contains(auditSummaries, summary => summary.Contains($"Accounting period updated: {created.Id}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostJournal_Blocks_WhenBusinessDateFallsInLockedPeriod()
    {
        await ResetAndSeedAsync(lockFebruaryPeriod: true);
        using var client = CreateClient("accounting.journals.view,accounting.journals.create,accounting.journals.post");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            businessDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            description = "Locked period governance test",
            referenceType = "Manual",
            referenceId = "RM03-BIZDATE-LCK",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 100m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 100m }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var postResponse = await client.PostAsync($"/api/accounting/journals/{created!.Id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
    }

    [Fact]
    public async Task FinancialYearEndpoints_EnforceBusinessIsolation()
    {
        await ResetAndSeedAsync(includeSecondBusiness: true);
        using var client = CreateClient("accounting.financial_years.view,accounting.financial_years.manage,accounting.periods.view");

        var crossBusinessPeriods = await client.GetAsync("/api/accounting/financial-years/9001/periods");
        Assert.Equal(HttpStatusCode.NotFound, crossBusinessPeriods.StatusCode);

        var crossBusinessUpdate = await client.PutAsJsonAsync("/api/accounting/financial-years/9001", new
        {
            yearCode = "FYX",
            name = "Cross",
            startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        Assert.Equal(HttpStatusCode.NotFound, crossBusinessUpdate.StatusCode);
    }

    [Fact]
    public async Task FinancialYearAndPeriodReads_PreserveBusinessScopeAndOrdering()
    {
        await ResetAndSeedAsync(includeSecondBusiness: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.FinancialYears.AddRange(
                new FinancialYear
                {
                    Id = 3002,
                    BusinessId = 1,
                    YearCode = "FY2025",
                    Name = "FY 2025",
                    StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new FinancialYear
                {
                    Id = 3003,
                    BusinessId = 1,
                    YearCode = "FY2027",
                    Name = "FY 2027",
                    StartDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 3301,
                FinancialYearId = 3001,
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

        using var client = CreateClient("accounting.financial_years.view,accounting.periods.view");
        var yearsResponse = await client.GetAsync("/api/accounting/financial-years");
        var years = await ReadData<List<FinancialYearResponse>>(yearsResponse);
        Assert.Equal(new long[] { 3003, 3001, 3002 }, years!.Select(year => year.Id));

        var periodsResponse = await client.GetAsync("/api/accounting/financial-years/3001/periods");
        var periods = await ReadData<List<AccountingPeriodResponse>>(periodsResponse);
        Assert.Equal(new long[] { 3101, 3201, 3301 }, periods!.Select(period => period.Id));
    }

    [Fact]
    public async Task FinancialYearCommands_PreserveCurrentYearScopeValidationAndAuditBehavior()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.FinancialYears.AddRange(
                new FinancialYear
                {
                    Id = 3002,
                    BusinessId = 1,
                    YearCode = "FY2027",
                    Name = "FY 2027",
                    StartDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                    IsCurrent = false,
                    IsClosed = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new FinancialYear
                {
                    Id = 3003,
                    BusinessId = 1,
                    YearCode = "FY2024",
                    Name = "FY 2024",
                    StartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                    IsCurrent = false,
                    IsClosed = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient("accounting.financial_years.manage");
        var create = await client.PostAsJsonAsync("/api/accounting/financial-years", new
        {
            yearCode = " FY2028 ",
            name = " FY 2028 ",
            startDate = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            isCurrent = true
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await ReadData<FinancialYearCommandResponse>(create);
        Assert.NotNull(created);
        Assert.Equal("FY2028", created!.YearCode);
        Assert.Equal("FY 2028", created.Name);
        Assert.True(created.IsCurrent);

        var update = await client.PutAsJsonAsync("/api/accounting/financial-years/3002", new
        {
            yearCode = " FY2027-UPDATED ",
            name = " FY 2027 Updated ",
            startDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadData<FinancialYearCommandResponse>(update);
        Assert.NotNull(updated);
        Assert.Equal("FY2027-UPDATED", updated!.YearCode);
        Assert.Equal("FY 2027 Updated", updated.Name);
        Assert.False(updated.IsCurrent);

        var activate = await client.PostAsync("/api/accounting/financial-years/3002/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var activated = await ReadData<FinancialYearCommandResponse>(activate);
        Assert.NotNull(activated);
        Assert.True(activated!.IsCurrent);

        var overlappingCreate = await client.PostAsJsonAsync("/api/accounting/financial-years", new
        {
            yearCode = "FY2027-OVERLAP",
            name = "FY 2027 Overlap",
            startDate = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            isCurrent = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, overlappingCreate.StatusCode);

        var closedUpdate = await client.PutAsJsonAsync("/api/accounting/financial-years/3003", new
        {
            yearCode = "FY2024-UPDATED",
            name = "FY 2024 Updated",
            startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        Assert.Equal(HttpStatusCode.BadRequest, closedUpdate.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var years = await verificationDb.FinancialYears.AsNoTracking()
            .Where(year => year.BusinessId == 1)
            .ToDictionaryAsync(year => year.Id);
        Assert.True(years[3002].IsCurrent);
        Assert.False(years[3001].IsCurrent);
        Assert.Equal(101L, years[3002].UpdatedBy);
        Assert.Null(years[3003].UpdatedBy);
        Assert.DoesNotContain(years.Values, year => year.YearCode == "FY2027-OVERLAP");
        Assert.Equal(1, years.Values.Count(year => year.IsCurrent));
        Assert.Equal(101L, years.Values.Single(year => year.YearCode == "FY2028").CreatedBy);

        var auditSummaries = await verificationDb.AuditEvents
            .AsNoTracking()
            .Select(auditEvent => auditEvent.ActionSummary)
            .ToListAsync();
        Assert.Contains(auditSummaries, summary => summary.Contains("Financial year created", StringComparison.Ordinal));
        Assert.Contains(auditSummaries, summary => summary.Contains("Financial year updated: 3002", StringComparison.Ordinal));
        Assert.Contains(auditSummaries, summary => summary.Contains("Financial year activated: 3002", StringComparison.Ordinal));
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

    private async Task ResetAndSeedAsync(bool lockFebruaryPeriod = false, bool includeSecondBusiness = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "RM03 Test Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "RM03 User",
            Email = "rm03-user@example.test",
            PasswordHash = "test",
            OutletId = 11,
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

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 3001,
            BusinessId = 1,
            YearCode = "FY2026",
            Name = "FY 2026",
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
                Id = 3101,
                FinancialYearId = 3001,
                PeriodNumber = 1,
                Name = "January",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new AccountingPeriod
            {
                Id = 3201,
                FinancialYearId = 3001,
                PeriodNumber = 2,
                Name = "February",
                StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                Status = lockFebruaryPeriod ? AccountingPeriodStatuses.Locked : AccountingPeriodStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        if (includeSecondBusiness)
        {
            db.Businesses.Add(new Business { Id = 2, Name = "Other Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.FinancialYears.Add(new FinancialYear
            {
                Id = 9001,
                BusinessId = 2,
                YearCode = "FYX",
                Name = "Other FY",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                IsCurrent = true,
                IsClosed = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(new UpdateInventorySettingsCommand(new InventorySettings
        {
            LowStockThreshold = 10,
            EnableLowStockAlerts = true,
            AllowNegativeStock = false,
            AutoReorder = false
        }));
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
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

    private sealed class FinancialYearResponse
    {
        public long Id { get; set; }
    }

    private sealed class FinancialYearCommandResponse
    {
        public long Id { get; set; }
        public string YearCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }

    private sealed class AccountingPeriodResponse
    {
        public long Id { get; set; }
    }

    private sealed class AccountingPeriodCommandResponse
    {
        public long Id { get; set; }
        public int PeriodNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
