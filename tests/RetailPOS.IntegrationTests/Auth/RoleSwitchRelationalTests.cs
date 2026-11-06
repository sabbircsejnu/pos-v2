using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Auth;

public sealed class RoleSwitchRelationalTests
{
    [Fact]
    public async Task SwitchAndReturnOwner_PreserveClaimsProfileAndPersistOnlyRefreshHashes()
    {
        await using var database = new SqliteAuthDatabase();
        var ownerRole = new Role
        {
            Id = 94001,
            Name = "BusinessOwner",
            Permissions = """["businesses.view","users.view"]"""
        };
        var actingRole = new Role { Id = 94002, Name = "Cashier", Permissions = """["pos.view"]""" };
        var business = new Business { Id = 94003, Name = "Role Switch Business", IsActive = true };
        var outlet = new Outlet { Id = 94004, BusinessId = business.Id, Name = "Main Outlet" };
        var user = new User
        {
            Id = 94005,
            BusinessId = business.Id,
            Name = "Role Switch Owner",
            Email = "role-switch@example.test",
            PasswordHash = "not-used",
            Role = ownerRole,
            IsActive = true
        };
        await using var db = new RetailPOSDbContext(database.Options);
        db.AddRange(ownerRole, actingRole, business, outlet, user);
        await db.SaveChangesAsync();

        var store = new AuthenticationStore(db);
        var settings = Options.Create(new JwtTokenSettings
        {
            Secret = "relational-role-switch-secret-at-least-32-bytes",
            Issuer = "role-switch-tests",
            Audience = "role-switch-tests",
            ExpirationMinutes = 45,
            RefreshTokenExpirationDays = 7
        });
        var issuer = new AuthenticationCredentialsIssuer(
            new JwtAuthenticationTokenService(settings),
            settings);
        var switchHandler = new SwitchRoleCommandHandler(store, issuer, new UserContextPolicy(store, new UserOutletAccessStore(db)));
        var switched = await switchHandler.Handle(
            new SwitchRoleCommand(user.Id, "cashier", outlet.Id),
            CancellationToken.None);

        Assert.Equal("Cashier", switched.User.RoleName);
        Assert.Equal("BusinessOwner", switched.User.RealRoleName);
        Assert.Equal("Cashier", switched.User.ActingRoleName);
        Assert.Equal(outlet.Id, switched.User.ActingOutletId);
        Assert.Equal(outlet.Name, switched.User.ActingOutletName);
        Assert.True(switched.User.IsRoleSwitched);
        Assert.True(switched.User.IsBusinessOwner);
        Assert.Equal("pos.view", Assert.Single(switched.User.Permissions));
        Assert.Equal(64, Convert.FromBase64String(switched.RefreshToken).Length);
        Assert.InRange(Math.Abs((switched.ExpiresAt - DateTime.UtcNow).TotalMinutes - 45), 0, 1);

        var switchedClaims = new JwtSecurityTokenHandler().ReadJwtToken(switched.AccessToken).Claims.ToArray();
        Assert.Contains(switchedClaims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == actingRole.Name);
        Assert.Contains(switchedClaims, claim =>
            claim.Type == AuthenticationClaimNames.RealRoleName && claim.Value == ownerRole.Name);
        Assert.Contains(switchedClaims, claim =>
            claim.Type == AuthenticationClaimNames.ActingRoleId && claim.Value == actingRole.Id.ToString());
        Assert.Contains(switchedClaims, claim =>
            claim.Type == AuthenticationClaimNames.ActingOutletId && claim.Value == outlet.Id.ToString());
        Assert.Contains(switchedClaims, claim =>
            claim.Type == AuthenticationClaimNames.IsRoleSwitched && claim.Value == "true");
        Assert.Contains(switchedClaims, claim => claim.Type == "permission" && claim.Value == "pos.view");
        Assert.DoesNotContain(switchedClaims, claim =>
            claim.Type == "permission" && claim.Value == "businesses.view");

        var switchedRefreshRow = await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
            token.TokenHash == HashToken(switched.RefreshToken));
        Assert.Equal(user.Id, switchedRefreshRow.UserId);
        Assert.NotEqual(switched.RefreshToken, switchedRefreshRow.TokenHash);
        Assert.Matches("^[0-9A-F]{64}$", switchedRefreshRow.TokenHash);
        Assert.InRange(Math.Abs((switchedRefreshRow.ExpiresAt - DateTime.UtcNow).TotalDays - 7), 0, 1);

        var returnHandler = new ReturnOwnerCommandHandler(store, issuer);
        var returned = await returnHandler.Handle(new ReturnOwnerCommand(user.Id), CancellationToken.None);
        Assert.Equal(ownerRole.Name, returned.User.RoleName);
        Assert.Equal(ownerRole.Name, returned.User.RealRoleName);
        Assert.Null(returned.User.ActingRoleName);
        Assert.Null(returned.User.ActingOutletId);
        Assert.False(returned.User.IsRoleSwitched);
        Assert.Equal(
            new[] { "businesses.view", "users.view" },
            returned.User.Permissions.OrderBy(permission => permission, StringComparer.Ordinal).ToArray());

        var returnedClaims = new JwtSecurityTokenHandler().ReadJwtToken(returned.AccessToken).Claims.ToArray();
        Assert.Contains(returnedClaims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == ownerRole.Name);
        Assert.DoesNotContain(returnedClaims, claim => claim.Type == AuthenticationClaimNames.IsRoleSwitched);

        var refreshRows = await db.UserRefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, refreshRows.Count);
        Assert.All(refreshRows, row => Assert.Null(row.RevokedAt));
        Assert.DoesNotContain(refreshRows, row =>
            row.TokenHash == switched.RefreshToken || row.TokenHash == returned.RefreshToken);
    }

    [Fact]
    public async Task SwitchRole_RejectsInvalidRoleAndForeignBusinessOutletWithoutIssuingTokens()
    {
        await using var database = new SqliteAuthDatabase();
        var ownerRole = new Role { Id = 94101, Name = "BusinessOwner", Permissions = "[]" };
        var cashierRole = new Role { Id = 94102, Name = "Cashier", Permissions = "[]" };
        var firstBusiness = new Business { Id = 94103, Name = "Owner Business", IsActive = true };
        var secondBusiness = new Business { Id = 94104, Name = "Foreign Business", IsActive = true };
        var foreignOutlet = new Outlet
        {
            Id = 94105,
            BusinessId = secondBusiness.Id,
            Name = "Foreign Outlet"
        };
        var owner = new User
        {
            Id = 94106,
            BusinessId = firstBusiness.Id,
            Name = "Owner",
            Email = "owner-role-switch@example.test",
            PasswordHash = "not-used",
            Role = ownerRole,
            IsActive = true
        };
        var nonOwner = new User
        {
            Id = 94107,
            Name = "Cashier",
            Email = "cashier-role-switch@example.test",
            PasswordHash = "not-used",
            Role = cashierRole,
            IsActive = true
        };

        await using var db = new RetailPOSDbContext(database.Options);
        db.AddRange(ownerRole, cashierRole, firstBusiness, secondBusiness, foreignOutlet, owner, nonOwner);
        await db.SaveChangesAsync();

        var store = new AuthenticationStore(db);
        var settings = Options.Create(new JwtTokenSettings
        {
            Secret = "relational-role-switch-secret-at-least-32-bytes",
            Issuer = "role-switch-tests",
            Audience = "role-switch-tests",
            ExpirationMinutes = 45,
            RefreshTokenExpirationDays = 7
        });
        var issuer = new AuthenticationCredentialsIssuer(
            new JwtAuthenticationTokenService(settings),
            settings);
        var handler = new SwitchRoleCommandHandler(store, issuer, new UserContextPolicy(store, new UserOutletAccessStore(db)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SwitchRoleCommand(owner.Id, " ", null), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new SwitchRoleCommand(owner.Id, "BusinessOwner", null), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new SwitchRoleCommand(owner.Id, "Cashier", foreignOutlet.Id), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new SwitchRoleCommand(nonOwner.Id, "Cashier", null), CancellationToken.None));

        Assert.Empty(await db.UserRefreshTokens.AsNoTracking().ToListAsync());
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class SqliteAuthDatabase : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(), $"RoleSwitch_{Guid.NewGuid():N}.db");

        public DbContextOptions<RetailPOSDbContext> Options { get; }

        public SqliteAuthDatabase()
        {
            Options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    DefaultTimeout = 30,
                    Pooling = false
                }.ToString())
                .Options;
            using var db = new RetailPOSDbContext(Options);
            db.Database.EnsureCreated();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new RetailPOSDbContext(Options))
            {
                await db.Database.EnsureDeletedAsync();
            }

            foreach (var path in new[] { _databasePath, $"{_databasePath}-shm", $"{_databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
