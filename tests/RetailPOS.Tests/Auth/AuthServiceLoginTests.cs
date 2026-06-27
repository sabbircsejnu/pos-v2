using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Auth;

/// <summary>
/// Tests for AuthService.LoginAsync covering outlet assignment behaviour.
/// 
/// Business rule: outlet assignment is NOT a prerequisite for login.
/// Outlet validation happens at the feature/transaction level only.
/// </summary>
public class AuthServiceLoginTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITokenService> _tokenMock;
    private readonly AuthService _authService;

    private const string TestPassword = "TestPassword123!";
    private const string TestToken = "test-access-token";
    private const string TestRefreshToken = "test-refresh-token";

    public AuthServiceLoginTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new RetailPOSDbContext(options);

        _tokenMock = new Mock<ITokenService>();
        _tokenMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>(), It.IsAny<Role?>(), It.IsAny<ActingRoleClaims?>()))
                  .Returns(TestToken);
        _tokenMock.Setup(t => t.GenerateRefreshToken())
                  .Returns(TestRefreshToken);

        var jwtOptions = Options.Create(new JwtSettings
        {
            Secret = "test-secret-at-least-32-characters-long!!",
            Issuer = "test",
            Audience = "test",
            ExpirationMinutes = 60
        });

        _authService = new AuthService(_db, _tokenMock.Object, jwtOptions);
    }

    public void Dispose() => _db.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private Role CreateRole(string name) => new Role
    {
        Name = name,
        Permissions = "[]"
    };

    private Outlet CreateOutlet() => new Outlet
    {
        Name = "Main Outlet",
        Address = "123 Test St"
    };

    private User CreateUser(string email, Role role, Outlet? outlet = null)
    {
        var user = new User
        {
            Name = "Test User",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword),
            IsActive = true,
            MustResetPassword = false,
            Role = role,
            Outlet = outlet,
            OutletId = outlet?.Id == 0 ? null : outlet?.Id
        };
        return user;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests: users WITH an outlet can log in
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("OutletManager")]
    [InlineData("SalesPerson")]
    [InlineData("AccountsAdmin")]
    [InlineData("WarehouseManager")]
    [InlineData("BusinessOwner")]
    public async Task Login_UserWithOutlet_Succeeds(string roleName)
    {
        var role = CreateRole(roleName);
        var outlet = CreateOutlet();
        _db.Roles.Add(role);
        _db.Outlets.Add(outlet);
        await _db.SaveChangesAsync();

        var user = CreateUser($"user-with-outlet-{roleName}@test.com", role, outlet);
        user.OutletId = outlet.Id;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Equal(TestToken, result.Token);
        Assert.Equal(outlet.Id, result.User.OutletId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests: users WITHOUT an outlet can still log in
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AccountsAdmin")]
    [InlineData("WarehouseManager")]
    [InlineData("BusinessOwner")]
    [InlineData("BusinessAdmin")]
    public async Task Login_UserWithoutOutlet_Succeeds(string roleName)
    {
        var role = CreateRole(roleName);
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser($"no-outlet-{roleName}@test.com", role, outlet: null);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Must not throw — outlet is optional at login
        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Equal(TestToken, result.Token);
        Assert.Null(result.User.OutletId);
    }

    [Fact]
    public async Task Login_OutletManagerWithoutOutlet_Succeeds()
    {
        // OutletManager requires outlet for transactional features,
        // but login must still succeed so the user can access the app
        // and the admin can assign an outlet without the account being locked out.
        var role = CreateRole("OutletManager");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("outlet-manager-no-outlet@test.com", role, outlet: null);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Equal(TestToken, result.Token);
        Assert.Null(result.User.OutletId);
    }

    [Fact]
    public async Task Login_SalesPersonWithoutOutlet_Succeeds()
    {
        var role = CreateRole("SalesPerson");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("salesperson-no-outlet@test.com", role, outlet: null);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Equal(TestToken, result.Token);
        Assert.Null(result.User.OutletId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests: other login validations still work
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_InactiveUser_ThrowsUnauthorized()
    {
        var role = CreateRole("SalesPerson");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("inactive@test.com", role);
        user.IsActive = false;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequestDto
            {
                Email = user.Email,
                Password = TestPassword
            }));

        Assert.Equal("Invalid email or password", ex.Message);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        var role = CreateRole("SalesPerson");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("wrongpw@test.com", role);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequestDto
            {
                Email = user.Email,
                Password = "WrongPassword!"
            }));

        Assert.Equal("Invalid email or password", ex.Message);
    }

    [Fact]
    public async Task Login_UnknownEmail_ThrowsUnauthorized()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequestDto
            {
                Email = "nobody@test.com",
                Password = TestPassword
            }));

        Assert.Equal("Invalid email or password", ex.Message);
    }

    [Fact]
    public async Task Login_MustResetPassword_ThrowsUnauthorized()
    {
        var role = CreateRole("SalesPerson");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("reset@test.com", role);
        user.MustResetPassword = true;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginRequestDto
            {
                Email = user.Email,
                Password = TestPassword
            }));

        Assert.Contains("Password setup is required", ex.Message);
    }

    [Fact]
    public async Task Login_ReturnsCorrectUserInfo()
    {
        var role = CreateRole("AccountsAdmin");
        var outlet = CreateOutlet();
        _db.Roles.Add(role);
        _db.Outlets.Add(outlet);
        await _db.SaveChangesAsync();

        var user = CreateUser("info@test.com", role, outlet);
        user.OutletId = outlet.Id;
        user.BusinessId = 42;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.Email, result.User.Email);
        Assert.Equal("AccountsAdmin", result.User.RoleName);
        Assert.Equal(42, result.User.BusinessId);
        Assert.Equal(outlet.Id, result.User.OutletId);
        Assert.Equal(outlet.Name, result.User.OutletName);
    }

    [Fact]
    public async Task Login_UserWithNoOutlet_ReturnsNullOutletInResponse()
    {
        var role = CreateRole("AccountsAdmin");
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var user = CreateUser("nooutlet-info@test.com", role, outlet: null);
        user.BusinessId = 10;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = user.Email,
            Password = TestPassword
        });

        Assert.Null(result.User.OutletId);
        Assert.Null(result.User.OutletName);
        Assert.Equal(TestToken, result.Token);
    }
}
