using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.ReadModels;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.BusinessAdministration;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessOnboardingWriteStoreTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Data Source=business-onboarding-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection _anchorConnection = null!;

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        await _anchorConnection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task CreateBusiness_CreatesTenantRolesInvitationsAndAccountingDefaultsAtomically()
    {
        var auditService = new Mock<IAuditService>();
        AuditEventInput? recordedAudit = null;
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => recordedAudit = input)
            .Returns(Task.CompletedTask);
        BusinessOnboardingReadModel response;

        await using (var context = CreateContext())
        {
            response = await CreateHandler(context, auditService.Object)
                .Handle(CreateRequest(createWarehouse: true), CancellationToken.None);
        }

        await using var verifyContext = CreateContext();
        var business = await verifyContext.Businesses.AsNoTracking().SingleAsync();
        var outlet = await verifyContext.Outlets.AsNoTracking().SingleAsync();
        var warehouse = await verifyContext.Warehouses.AsNoTracking().SingleAsync();
        var users = await verifyContext.Users.AsNoTracking().Include(user => user.Role).ToListAsync();
        var invitations = await verifyContext.UserInvitations.AsNoTracking().ToListAsync();
        var configuration = await verifyContext.AccountingConfigurations.AsNoTracking().SingleAsync();
        var outletConfiguration = await verifyContext.AccountingOutletConfigurations.AsNoTracking().SingleAsync();
        var financialYear = await verifyContext.FinancialYears.AsNoTracking().SingleAsync();
        var period = await verifyContext.AccountingPeriods.AsNoTracking().SingleAsync();

        Assert.Equal("Northwind Retail", business.Name);
        Assert.Equal("northwind@example.test", business.Email);
        Assert.Equal("555-0134", business.Phone);
        Assert.Equal("123 Market Street", business.Address);
        Assert.Equal("Northwind Retail", response.BusinessName);
        Assert.Equal(business.Id, response.BusinessId);
        Assert.Equal(outlet.Id, response.DefaultOutletId);
        Assert.Equal(warehouse.Id, response.DefaultWarehouseId);
        Assert.Equal("Main Outlet", outlet.Name);
        Assert.Equal("Central Warehouse", warehouse.Name);
        Assert.Equal(5, users.Count);
        Assert.Equal(5, response.Users.Count);
        Assert.Equal(5, invitations.Count);
        Assert.Equal(5, await verifyContext.BusinessFeatureSettings.CountAsync());
        Assert.Equal(AccountingDefaults.ChartOfAccounts.Count,
            await verifyContext.ChartAccounts.CountAsync(account => account.BusinessId == business.Id));
        Assert.Equal(financialYear.StartDate, period.StartDate);
        Assert.Equal(financialYear.EndDate, period.EndDate);
        Assert.Equal(AccountingPeriodStatuses.Open, period.Status);
        Assert.Equal(configuration.CashAccountId, outletConfiguration.OutletCashAccountId);
        Assert.Equal(configuration.PettyCashAccountId, outletConfiguration.OutletPettyCashAccountId);
        Assert.Equal(configuration.DefaultBankAccountId, outletConfiguration.OutletBankAccountId);
        Assert.Equal(business.Id, financialYear.BusinessId);
        Assert.Equal(business.Id, configuration.BusinessId);
        Assert.Equal(business.Id, outletConfiguration.BusinessId);

        var owner = Assert.Single(users, user => user.Role!.Name == "BusinessOwner");
        var outletManager = Assert.Single(users, user => user.Role!.Name == "OutletManager");
        var warehouseManager = Assert.Single(users, user => user.Role!.Name == "WarehouseManager");
        Assert.Equal(outletManager.Id, outlet.ManagerId);
        Assert.Equal(warehouseManager.Id, warehouse.ManagerId);
        Assert.Null(owner.OutletId);
        Assert.Equal(outlet.Id, outletManager.OutletId);
        Assert.All(users, user =>
        {
            Assert.True(user.IsActive);
            Assert.True(user.MustResetPassword);
            Assert.StartsWith("$2", user.PasswordHash);
        });
        Assert.All(invitations, invitation =>
        {
            Assert.Equal("onboarding", invitation.Purpose);
            Assert.Equal(64, invitation.TokenHash.Length);
            Assert.True(invitation.ExpiresAt > DateTime.UtcNow.AddDays(6));
        });
        foreach (var onboardedUser in response.Users)
        {
            var invitation = Assert.Single(invitations, item => item.UserId == onboardedUser.UserId);
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(onboardedUser.InvitationToken))),
                invitation.TokenHash);
            Assert.Equal(invitation.ExpiresAt, onboardedUser.InvitationExpiresAt);
        }

        var ownerRole = await verifyContext.Roles.AsNoTracking()
            .SingleAsync(role => role.Name == "BusinessOwner");
        Assert.Equal("[\"*\"]", ownerRole.Permissions);
        Assert.Equal(
            new[] { "Accounting Manager", "AccountsAdmin", "BusinessOwner", "OutletManager", "SalesPerson", "WarehouseManager" },
            await verifyContext.Roles.AsNoTracking().Select(role => role.Name).OrderBy(name => name).ToArrayAsync());

        Assert.NotNull(recordedAudit);
        Assert.Equal("Outlet accounting configuration created: " + outlet.Id, recordedAudit!.Summary);
        Assert.Equal(("AccountingOutletConfiguration", outletConfiguration.Id.ToString()), recordedAudit.PrimaryEntity);
        Assert.Equal(business.Id, recordedAudit.Metadata!["businessId"]);
        Assert.Equal(owner.Id, recordedAudit.Metadata["actorUserId"]);
        var auditEntity = Assert.Single(recordedAudit.Entities);
        Assert.Equal("AccountingOutletConfiguration", auditEntity.EntityType);
        Assert.Equal("Insert", auditEntity.OperationType);
        Assert.Contains(auditEntity.Fields, field => field.FieldName == nameof(AccountingOutletConfiguration.OutletId));
        auditService.Verify(
            service => service.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateBusiness_WithoutWarehouseCreatesOnlyFourUsersAndNoWarehouseRole()
    {
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using (var context = CreateContext())
        {
            var response = await CreateHandler(context, auditService.Object)
                .Handle(CreateRequest(createWarehouse: false), CancellationToken.None);
            Assert.Null(response.DefaultWarehouseId);
            Assert.Equal(4, response.Users.Count);
        }

        await using var verifyContext = CreateContext();
        Assert.Empty(await verifyContext.Warehouses.AsNoTracking().ToListAsync());
        Assert.Equal(4, await verifyContext.Users.CountAsync());
        Assert.False(await verifyContext.Roles.AnyAsync(role => role.Name == "WarehouseManager"));
    }

    [Fact]
    public async Task CreateBusiness_RollsBackAllCreatedRowsWhenAccountingAuditFails()
    {
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("forced accounting audit failure"));

        await using (var context = CreateContext())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateHandler(context, auditService.Object)
                    .Handle(CreateRequest(createWarehouse: true), CancellationToken.None));
            Assert.Equal("forced accounting audit failure", exception.Message);
        }

        await using var verifyContext = CreateContext();
        Assert.Empty(await verifyContext.Businesses.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.Outlets.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.Warehouses.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.Users.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.Roles.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.UserInvitations.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.BusinessFeatureSettings.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.ChartAccounts.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.AccountingConfigurations.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.AccountingOutletConfigurations.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.FinancialYears.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyContext.AccountingPeriods.AsNoTracking().ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _anchorConnection.DisposeAsync();
    }

    private CreateBusinessCommandHandler CreateHandler(
        RetailPOSDbContext context,
        IAuditService auditService)
    {
        var store = new BusinessOnboardingWriteStore(
            context,
            new AccountingConfigurationProvisioner(
                context,
                NullLogger<AccountingConfigurationProvisioner>.Instance),
            new TestPasswordHasher(),
            auditService,
            Mock.Of<IOperationalLocationSourceEventWriter>(),
            NullLogger<BusinessOnboardingWriteStore>.Instance);
        return new CreateBusinessCommandHandler(store);
    }

    private static CreateBusinessCommand CreateRequest(bool createWarehouse) => new(
        "  Northwind Retail  ",
        "  northwind@example.test  ",
        "  555-0134  ",
        "  123 Market Street  ",
        "  Owner One  ",
        "owner@example.test",
        "  Manager One  ",
        "manager@example.test",
        "  Sales One  ",
        "sales@example.test",
        "  Accounts One  ",
        "accounts@example.test",
        "  ",
        createWarehouse,
        "  Central Warehouse  ",
        createWarehouse ? "Warehouse Manager" : null,
        createWarehouse ? "warehouse@example.test" : null);

    private RetailPOSDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connectionString)
            .Options);

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public bool Verify(string password, string passwordHash) =>
            BCrypt.Net.BCrypt.Verify(password, passwordHash);

        public string Hash(string password, int? workFactor = null) =>
            BCrypt.Net.BCrypt.HashPassword(password, workFactor ?? 11);
    }
}
