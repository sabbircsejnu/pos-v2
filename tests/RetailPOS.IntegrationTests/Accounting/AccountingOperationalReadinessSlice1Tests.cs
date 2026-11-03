using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.Authorization;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.API.DTOs.Users;
using RetailPOS.API.Services;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class AccountingOperationalReadinessSlice1Tests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private const string Password = "TestPassword123!";

    public AccountingOperationalReadinessSlice1Tests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AccountingManagerRole_SeedsIdempotently_WithApprovedComposition_AndNoPermanentUser()
    {
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"Seed_{Guid.NewGuid()}").Options);

        await DbSeeder.SeedAsync(db);
        await DbSeeder.SeedAsync(db);

        var role = await db.Roles.SingleAsync(x => x.Name == AccountingManagerPermissions.RoleName);
        var permissions = JsonSerializer.Deserialize<List<string>>(role.Permissions) ?? [];

        Assert.Contains("accounting.reports.cash_flow", permissions);
        Assert.Contains("accounting.reports.profitability", permissions);
        Assert.Contains("accounting.manual_journals.submit", permissions);
        Assert.Contains("accounting.reconciliation.match", permissions);
        Assert.DoesNotContain("*", permissions);
        Assert.DoesNotContain("users.create", permissions);
        Assert.DoesNotContain("roles.edit", permissions);
        Assert.DoesNotContain("accounting.manual_journals.approve", permissions);
        Assert.DoesNotContain("accounting.journals.post", permissions);
        Assert.DoesNotContain("accounting.adjustments.approve", permissions);
        Assert.DoesNotContain("accounting.close.execute", permissions);
        Assert.False(await db.Users.Include(x => x.Role).AnyAsync(x => x.Role != null && x.Role.Name == AccountingManagerPermissions.RoleName));
    }

    [Fact]
    public async Task RoleAndUserGovernanceChanges_WriteAuditWithBeforeAfterFields()
    {
        await ResetAndSeedGovernanceAsync();
        using var client = CreateClient("roles.view,roles.create,roles.edit,users.view,users.create,users.edit");

        var createRole = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Temporary Accounting Role",
            Permissions = ["accounting.reports.cash_flow"]
        });
        Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
        var createdRole = await createRole.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(createdRole);

        var updateRole = await client.PutAsJsonAsync($"/api/roles/{createdRole!.Id}", new UpdateRoleDto
        {
            Name = "Temporary Accounting Role",
            Permissions = ["accounting.reports.cash_flow", "accounting.reports.profitability"]
        });
        Assert.Equal(HttpStatusCode.OK, updateRole.StatusCode);

        var createUser = await client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Governance User",
            Email = "governance-user@example.test",
            Password = Password,
            RoleId = createdRole.Id,
            BusinessId = 1,
            OutletId = 11,
            DefaultLocationType = "outlet",
            DefaultLocationId = 11,
            InventoryLocationAccessScope = CreateUserDto.ScopeAssignedOnly,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);
        var createdUser = await createUser.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(createdUser);

        var updateUser = await client.PutAsJsonAsync($"/api/users/{createdUser!.Id}", new UpdateUserDto
        {
            Name = "Governance User",
            Email = "governance-user@example.test",
            RoleId = 9002,
            BusinessId = 1,
            OutletId = 11,
            DefaultLocationType = "outlet",
            DefaultLocationId = 11,
            InventoryLocationAccessScope = UpdateUserDto.ScopeAssignedOnly,
            UserPermissions = ["accounting.reports.cash_book"],
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateUser.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvents = await db.AuditEvents.Include(x => x.Entities).ThenInclude(x => x.FieldChanges).Where(x => x.Module == AuditModule.Admin).ToListAsync();

        Assert.Contains(auditEvents, x => x.PrimaryEntityType == "Role" && x.ActionType == AuditActionType.Create);
        var roleUpdate = Assert.Single(auditEvents, x => x.PrimaryEntityType == "Role" && x.ActionType == AuditActionType.PermissionChange);
        Assert.Contains(roleUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "Permissions" && (x.NewValue?.Contains("accounting.reports.profitability") ?? false));
        var userUpdate = Assert.Single(auditEvents, x => x.PrimaryEntityType == "User" && x.ActionType == AuditActionType.PermissionChange);
        Assert.Contains(userUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "RoleId" && (x.OldValue?.Contains(createdRole.Id.ToString()) ?? false) && (x.NewValue?.Contains("9002") ?? false));
        Assert.Contains(userUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "PermissionOverrides" && (x.NewValue?.Contains("accounting.reports.cash_book") ?? false));
        Assert.Contains(userUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "IsActive" && (x.OldValue?.Contains("true", StringComparison.OrdinalIgnoreCase) ?? false) && (x.NewValue?.Contains("false", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.DoesNotContain(auditEvents.SelectMany(x => x.Entities).SelectMany(x => x.FieldChanges), x => x.FieldName.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccountingConfigurationOutletAndChartAccountChanges_WriteAuditWithBusinessScope()
    {
        await ResetAndSeedGovernanceAsync();
        using var client = CreateClient("accounting.configuration.view,accounting.configuration.edit,accounting.chart_accounts.view,accounting.chart_accounts.create,accounting.chart_accounts.edit");

        var config = await client.PutAsJsonAsync("/api/accounting/configuration", new AccountingConfigurationDto
        {
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
            SupplierAdvanceAccountId = 2011,
            CustomerAdvanceAccountId = 2101,
            OtherIncomeAccountId = 6001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3101,
            RetainedEarningsAccountId = 3201,
            RoundingDifferenceAccountId = 6101,
            CashDifferenceGainAccountId = 6201,
            CashDifferenceLossAccountId = 6211,
            VatPayableAccountId = 2001,
            VatReceivableAccountId = 1101,
            CardClearingAccountId = 1031,
            MobileMfsClearingAccountId = 1041,
            SettlementFeeExpenseAccountId = 6301,
            BankFeeExpenseAccountId = 6311,
            BankInterestIncomeAccountId = 6011,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);

        var outlet = await client.PutAsJsonAsync("/api/accounting/configuration/outlets/11", new AccountingOutletConfigurationDto { OutletId = 11, OutletCashAccountId = 1001, OutletPettyCashAccountId = 1011, OutletBankAccountId = 1021 });
        Assert.Equal(HttpStatusCode.OK, outlet.StatusCode);

        var createAccount = await client.PostAsJsonAsync("/api/accounting/chart-accounts", new CreateChartAccountDto { AccountCode = "8999", AccountName = "Audit Test Expense", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, AllowManualPosting = true, IsActive = true });
        Assert.Equal(HttpStatusCode.Created, createAccount.StatusCode);
        var account = await ReadData<ChartAccountDto>(createAccount);
        Assert.NotNull(account);

        var updateAccount = await client.PutAsJsonAsync($"/api/accounting/chart-accounts/{account!.Id}", new UpdateChartAccountDto { AccountName = "Audit Test Expense Updated", AllowManualPosting = true, IsActive = false });
        Assert.Equal(HttpStatusCode.OK, updateAccount.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvents = await db.AuditEvents.Include(x => x.Entities).ThenInclude(x => x.FieldChanges).Where(x => x.Module == AuditModule.Accounts).ToListAsync();

        Assert.Contains(auditEvents, x => x.PrimaryEntityType == "AccountingConfiguration" && x.BusinessId == 1);
        Assert.Contains(auditEvents, x => x.PrimaryEntityType == "AccountingOutletConfiguration" && x.BusinessId == 1);
        Assert.Contains(auditEvents, x => x.PrimaryEntityType == "ChartAccount" && x.ActionType == AuditActionType.Create && x.BusinessId == 1);
        var chartUpdate = Assert.Single(auditEvents, x => x.PrimaryEntityType == "ChartAccount" && x.ActionSummary.Contains("updated"));
        Assert.Contains(chartUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "AccountName" && (x.OldValue?.Contains("Audit Test Expense") ?? false) && (x.NewValue?.Contains("Audit Test Expense Updated") ?? false));
        Assert.Contains(chartUpdate.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "IsActive" && (x.NewValue?.Contains("false", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public async Task BankAccountCreate_WritesMasterDataAudit_AndFailedCreateWritesNoSuccessAudit()
    {
        var auditContext = new AuditContext();
        auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 1, 1, 11, 1, AuditSource.API, "POST", "/api/accounting/bank-accounts", "127.0.0.1", "127.0.0.1", null, true, null, null, null, null, "Actor", "Accounting Manager"));
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"BankAudit_{Guid.NewGuid()}").Options);
        db.Businesses.Add(new Business { Id = 1, Name = "Bank Audit" });
        db.ChartAccounts.Add(new ChartAccount { Id = 1021, BusinessId = 1, AccountCode = "1021", AccountName = "Bank", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true });
        await db.SaveChangesAsync();
        var audit = new AuditService(db, auditContext, MockServiceProvider(), NullLogger<AuditService>.Instance);
        var store = new BankAccountCommandStore(db, audit);

        var created = await store.CreateAsync(
            1,
            101,
            new CreateBankAccountInput(
                "Main Bank",
                1021,
                null,
                "private-reference-1234",
                true,
                null,
                null),
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(
            1,
            101,
            new CreateBankAccountInput("Duplicate", 1021, null, null, true, null, null),
            CancellationToken.None));

        var auditEvents = await db.AuditEvents.Include(x => x.Entities).ThenInclude(x => x.FieldChanges).Where(x => x.PrimaryEntityType == "BankAccount").ToListAsync();
        var auditEvent = Assert.Single(auditEvents);
        Assert.Equal(1, auditEvent.BusinessId);
        Assert.Equal(101, auditEvent.RealUserId);
        Assert.Equal(101, auditEvent.ActingUserId);
        Assert.Equal(AuditStatus.Success, auditEvent.Status);
        Assert.NotEqual(default, auditEvent.CreatedAt);
        Assert.Equal(created.Id.ToString(), auditEvent.PrimaryEntityId);
        Assert.Contains(auditEvent.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "ChartAccountId" && (x.NewValue?.Contains("1021") ?? false));
        Assert.Contains(auditEvent.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "DisplayName" && (x.NewValue?.Contains("Main Bank") ?? false));
        Assert.DoesNotContain(auditEvent.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName.Contains("Password", StringComparison.OrdinalIgnoreCase) || x.FieldName.Contains("Token", StringComparison.OrdinalIgnoreCase) || x.FieldName == "AccountReference");
        var storedAuditText = string.Join(" ", auditEvent.ActionSummary, auditEvent.Metadata, string.Join(" ", auditEvent.Entities.SelectMany(x => x.FieldChanges).SelectMany(x => new[] { x.OldValue, x.NewValue })));
        Assert.DoesNotContain("private-reference-1234", storedAuditText);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedGovernanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Operational Controls", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Roles.AddRange(
            new Role { Id = 9001, Name = AccountingManagerPermissions.RoleName, Permissions = JsonSerializer.Serialize(AccountingManagerPermissions.All), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Role { Id = 9002, Name = "Cashier", Permissions = JsonSerializer.Serialize(new[] { "sales.view" }), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Admin", Email = "admin-oc@example.test", PasswordHash = "test", RoleId = 9001, OutletId = 11, IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1011, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1031, "1030", "Card Clearing", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1041, "1040", "Mobile Clearing", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1101, "1100", "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(2011, "2010", "Supplier Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
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
            Account(6011, "6010", "Bank Interest", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6301, "6300", "Settlement Fee", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6311, "6310", "Bank Fee", ChartAccountTypes.Expense, NormalBalanceTypes.Debit));
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = code,
        AccountName = name,
        AccountType = type,
        NormalBalanceType = normalBalance,
        AllowManualPosting = true,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>();
        return payload?.Data;
    }

    private static IServiceProvider MockServiceProvider() => new Moq.Mock<IServiceProvider>().Object;

    private sealed class ApiEnvelope<T>
    {
        public T? Data { get; set; }
    }
}
