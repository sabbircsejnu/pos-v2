using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Authorization;
using Microsoft.Extensions.Options;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.DTOs.Users;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Auth;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class AccountingOperationalControlsAssessmentTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private const string Password = "TestPassword123!";

    public AccountingOperationalControlsAssessmentTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public void SeedAssets_DefineDedicatedAccountingManagerRole_WithBoundedSegregatedPermissions()
    {
        var root = ResolveRepoRoot();
        var seedData = File.ReadAllText(Path.Combine(root, "database", "seed-data.sql"));
        var seedV2 = File.ReadAllText(Path.Combine(root, "database", "seed-v2-roles.sql"));

        Assert.Contains("AccountsAdmin", seedData, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccountsAdmin", seedV2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accounting Manager", seedData, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accounting Manager", seedV2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accounting.reports.cash_flow", seedData, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accounting.reconciliation.match", seedData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Accounting Manager', '[\"*\"]", seedData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"users.create\"", ExtractAccountingManagerSeed(seedData), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"roles.edit\"", ExtractAccountingManagerSeed(seedData), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"accounting.manual_journals.approve\"", ExtractAccountingManagerSeed(seedData), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"accounting.adjustments.approve\"", ExtractAccountingManagerSeed(seedData), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"accounting.close.execute\"", ExtractAccountingManagerSeed(seedData), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposableFinanceUser_CanBeCreatedAssignedFinanceRoleLoginAndAccessPermittedReportOnly()
    {
        await ResetAndSeedUserFlowAsync();
        using var admin = CreateHeaderClient("users.view,users.create");

        var create = await admin.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Assessment Finance User",
            Email = "assessment.finance@example.test",
            Password = Password,
            RoleId = 5001,
            BusinessId = 1,
            OutletId = 11,
            DefaultLocationType = "outlet",
            DefaultLocationId = 11,
            InventoryLocationAccessScope = CreateUserDto.ScopeAssignedOnly,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequestDto { Email = "assessment.finance@example.test", Password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginDto);
        Assert.Equal(AccountingManagerPermissions.RoleName, loginDto!.User.RoleName);
        Assert.Contains("accounting.reports.cash_flow", loginDto!.User.Permissions);
        Assert.Contains("accounting.manual_journals.submit", loginDto.User.Permissions);
        Assert.DoesNotContain("users.create", loginDto.User.Permissions);
        Assert.DoesNotContain("roles.edit", loginDto.User.Permissions);
        Assert.DoesNotContain("accounting.manual_journals.approve", loginDto.User.Permissions);
        Assert.DoesNotContain("accounting.adjustments.approve", loginDto.User.Permissions);
        Assert.DoesNotContain("accounting.close.execute", loginDto.User.Permissions);

        using var finance = CreateHeaderClient(string.Join(',', loginDto.User.Permissions), loginDto.User.Id, loginDto.User.RoleName ?? "AccountsAdmin");
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/accounting/reports/cash-flow?dateFrom=2026-09-01&dateTo=2026-09-30")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task RolePermissionRemoval_AffectsRefreshAndMe_ButPreviouslyIssuedAccessTokenClaimsRemainStatic()
    {
        await using var db = CreateAuthDb();
        var role = new Role { Id = 6001, Name = "AssessmentFinance", Permissions = JsonSerializer.Serialize(new[] { "accounting.reports.cash_flow" }) };
        var user = new User
        {
            Id = 7001,
            BusinessId = 1,
            Name = "Refresh User",
            Email = "refresh-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            Role = role,
            IsActive = true,
            MustResetPassword = false
        };
        db.Businesses.Add(new Business { Id = 1, Name = "Assessment" });
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var options = Options.Create(new JwtSettings
        {
            Secret = "test-secret-at-least-32-characters-long!!",
            Issuer = "test",
            Audience = "test",
            ExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7
        });
        var authStore = new AuthenticationStore(db);
        var credentialsIssuer = CreateCredentialsIssuer(options);
        var auth = CreateAuthenticationCommandHarness(db, options);
        var login = await new LoginCommandHandler(authStore, new BCryptPasswordHasher(), credentialsIssuer)
            .Handle(new LoginCommand(user.Email, Password, rememberMe: false), CancellationToken.None);
        Assert.Contains("accounting.reports.cash_flow", login.User.Permissions);
        Assert.Contains("accounting.reports.cash_flow", ReadTokenPermissions(login.AccessToken));

        role.Permissions = "[]";
        await db.SaveChangesAsync();

        var currentUser = await new GetCurrentUserQueryHandler(authStore)
            .Handle(new GetCurrentUserQuery(user.Id), CancellationToken.None);
        Assert.NotNull(currentUser);
        Assert.DoesNotContain("accounting.reports.cash_flow", currentUser!.Permissions);
        Assert.Contains("accounting.reports.cash_flow", ReadTokenPermissions(login.AccessToken));

        var refreshed = await auth.RefreshTokenAsync(new RefreshTokenRequestDto { Token = login.AccessToken, RefreshToken = login.RefreshToken });
        Assert.DoesNotContain("accounting.reports.cash_flow", refreshed.User.Permissions);
        Assert.DoesNotContain("accounting.reports.cash_flow", ReadTokenPermissions(refreshed.AccessToken));
    }

    private HttpClient CreateHeaderClient(string permissions, long userId = 101, string role = "BusinessAdmin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedUserFlowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Assessment Business", IsActive = true });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "A" });
        db.Roles.Add(new Role { Id = 5001, Name = AccountingManagerPermissions.RoleName, Permissions = JsonSerializer.Serialize(AccountingManagerPermissions.All) });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Admin", Email = "admin-assessment@example.test", PasswordHash = "test", OutletId = 11, IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly });
        db.ChartAccounts.AddRange(
            Account(1001, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1011, "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, CashAccountId = 1001, PettyCashAccountId = 1011, DefaultBankAccountId = 1021, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear });
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, string name, string type, string normalBalance) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = id.ToString(),
        AccountName = name,
        AccountType = type,
        NormalBalanceType = normalBalance,
        IsActive = true,
        AllowManualPosting = true
    };

    private static AuthenticationCommandTestHarness CreateAuthenticationCommandHarness(RetailPOSDbContext db)
    {
        var options = Options.Create(new JwtSettings { Secret = "test-secret-at-least-32-characters-long!!", Issuer = "test", Audience = "test", ExpirationMinutes = 60, RefreshTokenExpirationDays = 7 });
        return CreateAuthenticationCommandHarness(db, options);
    }

    private static AuthenticationCredentialsIssuer CreateCredentialsIssuer(IOptions<JwtSettings> options)
    {
        var tokenSettings = CreateTokenSettings(options);
        return new AuthenticationCredentialsIssuer(
            new JwtAuthenticationTokenService(tokenSettings),
            tokenSettings);
    }

    private static AuthenticationCommandTestHarness CreateAuthenticationCommandHarness(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options) =>
        new(db, options);

    private static IOptions<JwtTokenSettings> CreateTokenSettings(IOptions<JwtSettings> options) =>
        Options.Create(new JwtTokenSettings
        {
            Secret = options.Value.Secret,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            ExpirationMinutes = options.Value.ExpirationMinutes,
            RefreshTokenExpirationDays = options.Value.RefreshTokenExpirationDays
        });

    private static RetailPOSDbContext CreateAuthDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"AccountingAssessment_{Guid.NewGuid()}").Options);

    private static IReadOnlyList<string> ReadTokenPermissions(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return jwt.Claims.Where(x => x.Type == "permission").Select(x => x.Value).ToList();
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "RetailPOS.slnx");
            if (File.Exists(marker)) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not resolve repository root from test base directory.");
    }

    private static string ExtractAccountingManagerSeed(string content)
    {
        var start = content.IndexOf("'Accounting Manager'", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        var end = content.IndexOf("'OutletManager'", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? content[start..] : content[start..end];
    }
}
