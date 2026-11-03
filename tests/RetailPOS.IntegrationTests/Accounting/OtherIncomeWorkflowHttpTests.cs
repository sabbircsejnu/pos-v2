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

public class OtherIncomeWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OtherIncomeWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Submit_Approve_Lifecycle_Works()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.other_income.view,accounting.other_income.create,accounting.other_income.edit,accounting.other_income.submit,accounting.other_income.approve");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Rebate",
            amount = 90m,
            paymentMethod = "Bank",
            paymentAccountId = 1021,
            description = "Vendor rebate",
            outletId = 11
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<OtherIncomeResponse>(createResponse);
        Assert.NotNull(created);
        Assert.Equal(OtherIncomeStatuses.Draft, created!.Status);

        var submitResponse = await client.PostAsync($"/api/accounting/incomes/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var approveResponse = await client.PostAsJsonAsync($"/api/accounting/incomes/{created.Id}/approve", new { });
        Assert.True(
            approveResponse.StatusCode == HttpStatusCode.OK,
            await approveResponse.Content.ReadAsStringAsync());
        var approved = await ReadData<OtherIncomeResponse>(approveResponse);
        Assert.NotNull(approved);
        Assert.Equal(OtherIncomeStatuses.Approved, approved!.Status);
        Assert.NotNull(approved.JournalEntryId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.OtherIncomeEntries.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(101L, persisted.SubmittedBy);
        Assert.NotNull(persisted.SubmittedAt);
        Assert.True(persisted.UpdatedAt >= persisted.SubmittedAt!.Value);
        Assert.Equal(101L, persisted.UpdatedBy);

        var submitAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("submitted", StringComparison.Ordinal));
        Assert.Equal(1L, submitAudit.BusinessId);
        Assert.Equal(101L, submitAudit.RealUserId);
        Assert.Contains(submitAudit.Entities.SelectMany(x => x.FieldChanges), x =>
            x.FieldName == "Status"
            && x.OldValue?.Contains(OtherIncomeStatuses.Draft) == true
            && x.NewValue?.Contains(OtherIncomeStatuses.Submitted) == true);

        var approveAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("approved", StringComparison.Ordinal));
        Assert.Equal(1L, approveAudit.BusinessId);
        Assert.Equal(101L, approveAudit.RealUserId);
        Assert.Null(approveAudit.ActingUserId);
        Assert.NotEqual(default, approveAudit.CreatedAt);
        Assert.Contains(approveAudit.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "JournalEntryId" && (x.NewValue?.Contains(approved.JournalEntryId.Value.ToString()) ?? false));
    }

    [Fact]
    public async Task SearchAndDetail_ReadsPreserveBusinessScopedProjectionAndPaging()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.other_income.view,accounting.other_income.create");

        var firstResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "First rebate",
            amount = 90m,
            outletId = 11
        });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await ReadData<OtherIncomeResponse>(firstResponse);
        Assert.NotNull(first);

        var secondResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Second rebate",
            amount = 120m,
            outletId = 11
        });
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await ReadData<OtherIncomeResponse>(secondResponse);
        Assert.NotNull(second);

        var detailResponse = await client.GetAsync($"/api/accounting/incomes/{second!.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadData<OtherIncomeResponse>(detailResponse);
        Assert.NotNull(detail);
        Assert.Equal("Second rebate", detail!.IncomeCategory);
        Assert.Equal(120m, detail.Amount);

        var searchResponse = await client.GetAsync(
            "/api/accounting/incomes?startDate=2026-04-01&endDate=2026-04-30&status=Draft&outletId=11&pageNumber=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var search = await ReadData<OtherIncomeListResponse>(searchResponse);
        Assert.NotNull(search);
        Assert.Equal(2, search!.TotalCount);
        Assert.Equal(1, search.PageSize);
        Assert.Equal(second.Id, Assert.Single(search.Items).Id);

        var postSearchResponse = await client.PostAsJsonAsync("/api/accounting/incomes/search", new
        {
            startDate = "2026-04-01",
            endDate = "2026-04-30",
            status = "Draft",
            outletId = 11,
            pageNumber = 2,
            pageSize = 1
        });
        Assert.Equal(HttpStatusCode.OK, postSearchResponse.StatusCode);
        var postSearch = await ReadData<OtherIncomeListResponse>(postSearchResponse);
        Assert.NotNull(postSearch);
        Assert.Equal(2, postSearch!.TotalCount);
        Assert.Equal(first!.Id, Assert.Single(postSearch.Items).Id);
    }

    [Fact]
    public async Task Reject_SubmittedEntryPreservesReasonActorAndAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            "accounting.other_income.create,accounting.other_income.submit,accounting.other_income.reject");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Rebate",
            amount = 90m,
            paymentAccountId = 1021
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<OtherIncomeResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await client.PostAsync(
            $"/api/accounting/incomes/{created!.Id}/submit",
            null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/accounting/incomes/{created.Id}/reject",
            new { reason = "  Missing deposit slip  " });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = await ReadData<OtherIncomeResponse>(rejectResponse);
        Assert.NotNull(rejected);
        Assert.Equal(OtherIncomeStatuses.Rejected, rejected!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.OtherIncomeEntries.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(OtherIncomeStatuses.Rejected, persisted.Status);
        Assert.Equal("Missing deposit slip", persisted.RejectionReason);
        Assert.Equal(101L, persisted.RejectedBy);
        Assert.NotNull(persisted.RejectedAt);
        Assert.True(persisted.UpdatedAt >= persisted.RejectedAt!.Value);
        Assert.Equal(101L, persisted.UpdatedBy);
        Assert.Null(persisted.JournalEntryId);

        var rejectAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary == $"Other income rejected: {created.Id}");
        Assert.Equal(1L, rejectAudit.BusinessId);
        Assert.Equal(101L, rejectAudit.RealUserId);
        Assert.Null(rejectAudit.ActingUserId);
        var changes = rejectAudit.Entities.SelectMany(x => x.FieldChanges).ToArray();
        Assert.Contains(changes, x =>
            x.FieldName == "Status"
            && x.OldValue?.Contains(OtherIncomeStatuses.Submitted) == true
            && x.NewValue?.Contains(OtherIncomeStatuses.Rejected) == true);
        Assert.Contains(changes, x =>
            x.FieldName == "RejectionReason"
            && x.NewValue?.Contains("Missing deposit slip") == true);
    }

    [Fact]
    public async Task Cancel_DraftEntryPreservesReasonActorAndAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.other_income.create,accounting.other_income.cancel");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Rebate",
            amount = 90m,
            paymentAccountId = 1021
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<OtherIncomeResponse>(createResponse);
        Assert.NotNull(created);

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/accounting/incomes/{created!.Id}/cancel",
            new { reason = "  Created by mistake  " });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await ReadData<OtherIncomeResponse>(cancelResponse);
        Assert.NotNull(cancelled);
        Assert.Equal(OtherIncomeStatuses.Cancelled, cancelled!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.OtherIncomeEntries.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(OtherIncomeStatuses.Cancelled, persisted.Status);
        Assert.Equal("Created by mistake", persisted.CancellationReason);
        Assert.Equal(101L, persisted.CancelledBy);
        Assert.NotNull(persisted.CancelledAt);
        Assert.True(persisted.UpdatedAt >= persisted.CancelledAt!.Value);
        Assert.Equal(101L, persisted.UpdatedBy);
        Assert.Null(persisted.JournalEntryId);

        var cancelAudit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary == $"Other income cancelled: {created.Id}");
        Assert.Equal(1L, cancelAudit.BusinessId);
        Assert.Equal(101L, cancelAudit.RealUserId);
        Assert.Null(cancelAudit.ActingUserId);
        var changes = cancelAudit.Entities.SelectMany(x => x.FieldChanges).ToArray();
        Assert.Contains(changes, x =>
            x.FieldName == "Status"
            && x.OldValue?.Contains(OtherIncomeStatuses.Draft) == true
            && x.NewValue?.Contains(OtherIncomeStatuses.Cancelled) == true);
        Assert.Contains(changes, x =>
            x.FieldName == "CancellationReason"
            && x.NewValue?.Contains("Created by mistake") == true);
    }

    [Fact]
    public async Task Create_PreservesNormalizedFinancialFieldsAndAudit()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.other_income.create");

        var response = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 17, 30, 0, DateTimeKind.Utc),
            incomeCategory = "  Rebate  ",
            incomeAccountId = 6001,
            amount = 110m,
            vatRate = 10m,
            vatAmount = 10m,
            taxCode = "VAT10",
            taxInclusive = true,
            paymentMethod = "  Bank  ",
            paymentAccountId = 1021,
            referenceNumber = "  REF-14  ",
            description = "  Vendor rebate  ",
            outletId = 11
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await ReadData<OtherIncomeResponse>(response);
        Assert.NotNull(created);
        Assert.Equal(OtherIncomeStatuses.Draft, created!.Status);
        Assert.Equal(1L, created.BusinessId);
        Assert.Equal(new DateTime(2026, 4, 14), created.IncomeDate);
        Assert.Equal("Rebate", created.IncomeCategory);
        Assert.Equal(6001L, created.IncomeAccountId);
        Assert.Equal(110m, created.Amount);
        Assert.Equal("Bank", created.PaymentMethod);
        Assert.Equal(1021L, created.PaymentAccountId);
        Assert.Equal("REF-14", created.ReferenceNumber);
        Assert.Equal("Vendor rebate", created.Description);
        Assert.Equal(11L, created.OutletId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var entry = await db.OtherIncomeEntries.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(10m, entry.VatRate);
        Assert.Equal(10m, entry.VatAmount);
        Assert.Equal(100m, entry.TaxableAmount);
        Assert.Equal("VAT10", entry.TaxCode);
        Assert.True(entry.TaxInclusive);
        Assert.Equal(101L, entry.CreatedBy);
        Assert.Equal(101L, entry.UpdatedBy);
        Assert.NotEqual(default, entry.CreatedAt);
        Assert.Equal(entry.CreatedAt, entry.UpdatedAt);

        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("created", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        var changes = audit.Entities.SelectMany(x => x.FieldChanges).ToArray();
        Assert.Contains(changes, x => x.FieldName == "TaxableAmount" && x.NewValue?.Contains("100") == true);
        Assert.Contains(changes, x => x.FieldName == "IncomeAccountId" && x.NewValue?.Contains("6001") == true);
        Assert.Contains(changes, x => x.FieldName == "PaymentAccountId" && x.NewValue?.Contains("1021") == true);
    }

    [Fact]
    public async Task Update_RejectedEntryPreservesRulesAndClearsRejectionMetadata()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            "accounting.other_income.create,accounting.other_income.edit,accounting.other_income.submit,accounting.other_income.reject");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Commission",
            incomeAccountId = 6001,
            amount = 100m,
            vatRate = 5m,
            vatAmount = 5m,
            taxCode = "VAT5",
            paymentAccountId = 1021,
            referenceNumber = "INC-OLD",
            outletId = 11
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<OtherIncomeResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await client.PostAsync($"/api/accounting/incomes/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/accounting/incomes/{created.Id}/reject",
            new { reason = "Missing receipt" });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounting/incomes/{created.Id}",
            new
            {
                incomeDate = new DateTime(2026, 4, 15, 17, 0, 0, DateTimeKind.Utc),
                incomeCategory = "  Rebate  ",
                incomeAccountId = 5201,
                amount = 130m,
                vatRate = 10m,
                vatAmount = 10m,
                taxCode = "VAT10",
                taxInclusive = true,
                paymentMethod = "  Cash  ",
                paymentAccountId = 1001,
                referenceNumber = "  INC-NEW  ",
                description = "  Updated rebate  ",
                outletId = 11
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadData<OtherIncomeResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal(OtherIncomeStatuses.Rejected, updated!.Status);
        Assert.Equal(new DateTime(2026, 4, 15), updated.IncomeDate);
        Assert.Equal("Rebate", updated.IncomeCategory);
        Assert.Equal(5201L, updated.IncomeAccountId);
        Assert.Equal(130m, updated.Amount);
        Assert.Equal("Cash", updated.PaymentMethod);
        Assert.Equal(1001L, updated.PaymentAccountId);
        Assert.Equal("INC-NEW", updated.ReferenceNumber);
        Assert.Equal("Updated rebate", updated.Description);
        Assert.Null(updated.RejectedAt);
        Assert.Null(updated.RejectionReason);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var entry = await db.OtherIncomeEntries.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(120m, entry.TaxableAmount);
        Assert.True(entry.TaxInclusive);
        Assert.Equal("VAT10", entry.TaxCode);
        Assert.Equal(101L, entry.UpdatedBy);
        Assert.Null(entry.RejectedBy);
        Assert.Null(entry.RejectedAt);
        Assert.Null(entry.RejectionReason);

        var audit = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "OtherIncomeEntry"
                && x.PrimaryEntityId == created.Id.ToString()
                && x.ActionSummary.Contains("updated", StringComparison.Ordinal));
        Assert.Equal(1L, audit.BusinessId);
        Assert.Equal(101L, audit.RealUserId);
        var changes = audit.Entities.SelectMany(x => x.FieldChanges).ToArray();
        Assert.Contains(changes, x =>
            x.FieldName == "Amount"
            && x.OldValue?.Contains("100") == true
            && x.NewValue?.Contains("130") == true);
        Assert.Contains(changes, x =>
            x.FieldName == "VatAmount"
            && x.OldValue?.Contains("5") == true
            && x.NewValue?.Contains("10") == true);
        Assert.Contains(changes, x => x.FieldName == "IncomeAccountId");
    }

    [Fact]
    public async Task Reject_RequiresPermission()
    {
        await ResetAndSeedAsync();
        using var fullClient = CreateClient("accounting.other_income.view,accounting.other_income.create,accounting.other_income.edit,accounting.other_income.submit");

        var createResponse = await fullClient.PostAsJsonAsync("/api/accounting/incomes", new
        {
            incomeDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc),
            incomeCategory = "Commission",
            amount = 30m,
            paymentAccountId = 1021
        });
        var created = await ReadData<OtherIncomeResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await fullClient.PostAsync($"/api/accounting/incomes/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var limitedClient = CreateClient("accounting.other_income.view");
        var rejectResponse = await limitedClient.PostAsJsonAsync($"/api/accounting/incomes/{created.Id}/reject", new { reason = "Missing evidence" });
        Assert.Equal(HttpStatusCode.Forbidden, rejectResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(db.AuditEvents.Where(x => x.PrimaryEntityType == "OtherIncomeEntry" && x.PrimaryEntityId == created.Id.ToString() && x.ActionSummary.Contains("rejected", StringComparison.Ordinal)));
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

        db.Businesses.Add(new Business { Id = 1, Name = "Other Income HTTP Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Income User",
            Email = "income-user@example.test",
            PasswordHash = "test",
            OutletId = 11,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

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
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
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
            Id = 9801,
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
            Id = 9802,
            FinancialYearId = 9801,
            PeriodNumber = 4,
            Name = "April",
            StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
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

    private sealed class OtherIncomeResponse
    {
        public long Id { get; set; }
        public long BusinessId { get; set; }
        public DateTime IncomeDate { get; set; }
        public string IncomeCategory { get; set; } = string.Empty;
        public long? IncomeAccountId { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public long? PaymentAccountId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public long? OutletId { get; set; }
        public long? JournalEntryId { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
    }

    private sealed class OtherIncomeListResponse
    {
        public List<OtherIncomeResponse> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}