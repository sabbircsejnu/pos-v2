using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Application.Authentication.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Auth;

public sealed class AuthenticationLifecycleCharacterizationTests
{
    private const string Secret = "characterization-only-secret-key-32-bytes";
    private const string Issuer = "characterization-issuer";
    private const string Audience = "characterization-audience";
    private const string Password = "Characterization-Pass-123";

    [Fact]
    public async Task Login_EmitsCurrentClaimContractAndStoresOnlyRefreshTokenHash()
    {
        await using var db = CreateDb();
        var role = new Role
        {
            Id = 401,
            Name = "AccountsAdmin",
            Permissions = """["users.view","inventory.adjust","*"]"""
        };
        var business = new Business { Id = 901, Name = "Characterization Business", IsActive = true };
        var outlet = new Outlet { Id = 902, BusinessId = business.Id, Name = "Characterization Outlet" };
        var user = new User
        {
            Id = 903,
            BusinessId = business.Id,
            Name = "Auth Characterization",
            Email = "auth-characterization@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = role,
            Outlet = outlet,
            PermissionOverrides = """["pos.sale.backdate","users.view"]""",
            IsActive = true
        };
        db.AddRange(business, outlet, role, user);
        await db.SaveChangesAsync();

        var options = CreateJwtOptions(expirationMinutes: 45, refreshDays: 3);
        var response = await LoginAsync(db, options, user.Email, Password, rememberMe: true);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        var claims = jwt.Claims.GroupBy(claim => claim.Type)
            .ToDictionary(group => group.Key, group => group.Select(claim => claim.Value).ToArray());

        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Contains(Audience, jwt.Audiences);
        Assert.Equal("903", claims[ClaimTypes.NameIdentifier].Single());
        Assert.Equal("Auth Characterization", claims[ClaimTypes.Name].Single());
        Assert.Equal("auth-characterization@example.test", claims[ClaimTypes.Email].Single());
        Assert.Equal("903", claims["userId"].Single());
        Assert.Equal("401", claims["roleId"].Single());
        Assert.Equal("401", claims[RoleSwitchClaims.RealRoleId].Single());
        Assert.Equal("AccountsAdmin", claims[RoleSwitchClaims.RealRoleName].Single());
        Assert.Equal("AccountsAdmin", claims[ClaimTypes.Role].Single());
        Assert.Equal("901", claims["businessId"].Single());
        Assert.Equal("902", claims["outletId"].Single());
        Assert.Contains("*", claims["permission"]);
        Assert.Contains("inventory.adjust", claims["permission"]);
        Assert.Contains("sales.backdate", claims["permission"]);
        Assert.DoesNotContain("pos.sale.backdate", claims["permission"]);
        Assert.False(claims.ContainsKey(RoleSwitchClaims.IsRoleSwitched));
        Assert.Contains("exp", claims.Keys);
        Assert.DoesNotContain("iat", claims.Keys);
        Assert.DoesNotContain("nbf", claims.Keys);
        Assert.DoesNotContain("jti", claims.Keys);
        Assert.True(Math.Abs((jwt.ValidTo - response.ExpiresAt).TotalSeconds) < 3);
        Assert.Equal(64, Convert.FromBase64String(response.RefreshToken).Length);
        Assert.Equal("Auth Characterization", response.User.Name);
        Assert.Equal(45, (int)Math.Round((response.ExpiresAt - DateTime.UtcNow).TotalMinutes));
        Assert.Equal(3, (int)Math.Round((await db.UserRefreshTokens.SingleAsync()).ExpiresAt
            .Subtract(DateTime.UtcNow).TotalDays));

        var storedToken = await db.UserRefreshTokens.SingleAsync();
        Assert.Equal(HashToken(response.RefreshToken), storedToken.TokenHash);
        Assert.NotEqual(response.RefreshToken, storedToken.TokenHash);
        Assert.Null(storedToken.RevokedAt);
    }

    [Fact]
    public async Task Login_UsesSameCredentialFailureForUnknownAndWrongPasswordWithoutMutation()
    {
        await using var db = CreateDb();
        var role = new Role { Id = 410, Name = "Cashier", Permissions = "[]" };
        var user = new User
        {
            Id = 411,
            Name = "Login User",
            Email = "login-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = role,
            IsActive = true
        };
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var options = CreateJwtOptions();
        var originalHash = user.PasswordHash;

        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LoginAsync(db, options, user.Email, "Wrong-Password-123"));
        var unknownUser = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LoginAsync(db, options, "unknown@example.test", Password));
        var differentCase = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LoginAsync(db, options, user.Email.ToUpperInvariant(), Password));

        Assert.Equal(wrongPassword.Message, unknownUser.Message);
        Assert.Equal(wrongPassword.Message, differentCase.Message);
        Assert.Equal("Invalid email or password", wrongPassword.Message);
        Assert.Empty(await db.UserRefreshTokens.ToListAsync());

        user.IsActive = false;
        await db.SaveChangesAsync();
        var inactive = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LoginAsync(db, options, user.Email, Password));
        Assert.Equal(wrongPassword.Message, inactive.Message);
        Assert.Empty(await db.UserRefreshTokens.ToListAsync());
        user.IsActive = true;
        user.MustResetPassword = true;
        await db.SaveChangesAsync();
        var resetRequired = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LoginAsync(db, options, user.Email, Password));
        Assert.Contains("Password setup is required", resetRequired.Message);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Empty(await db.UserRefreshTokens.ToListAsync());
    }

    [Fact]
    public void AuthenticationCommandsDoNotExposeSubmittedSecretsThroughToString()
    {
        const string password = "never-log-this-password";
        const string invitation = "never-log-this-invitation";

        Assert.DoesNotContain(password, new LoginCommand("safe@example.test", password, false).ToString());
        Assert.DoesNotContain(password, new RegisterCommand("Safe", "safe@example.test", password, password).ToString());
        Assert.DoesNotContain(invitation,
            new CompleteInvitationCommand(invitation, password).ToString());
        Assert.DoesNotContain(password,
            new ChangeUserPasswordCommand(12, password, password, password).ToString());
        Assert.DoesNotContain(password,
            new ChangeMyPasswordCommand(password, password, password).ToString());
        Assert.DoesNotContain(invitation,
            new RefreshTokenCommand(invitation, password).ToString());
        Assert.DoesNotContain(password,
            new LogoutCommand(12, password).ToString());
    }

    [Fact]
    public async Task Refresh_RotatesHashedTokenRecomputesPermissionsAndLogoutLeavesAccessJwtValid()
    {
        await using var db = CreateDb();
        var role = new Role
        {
            Id = 420,
            Name = "AccountsAdmin",
            Permissions = """["users.view","sales.view"]"""
        };
        var user = new User
        {
            Id = 421,
            Name = "Refresh User",
            Email = "refresh-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = role,
            IsActive = true
        };
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var options = CreateJwtOptions(expirationMinutes: 30, refreshDays: 4);
        var auth = CreateAuthenticationCommandHarness(db, options);
        var tokenService = CreateTokenService(options);
        var login = await LoginAsync(db, options, user.Email, Password);
        var oldAccessToken = login.AccessToken;
        var oldRefreshToken = login.RefreshToken;

        role.Permissions = """["users.view"]""";
        await db.SaveChangesAsync();
        var refreshed = await auth.RefreshTokenAsync(new RefreshTokenRequestDto
        {
            Token = oldAccessToken,
            RefreshToken = oldRefreshToken
        });

        var previous = await db.UserRefreshTokens.SingleAsync(token => token.TokenHash == HashToken(oldRefreshToken));
        var replacement = await db.UserRefreshTokens.SingleAsync(token => token.TokenHash == HashToken(refreshed.RefreshToken));
        Assert.Equal("Rotated", previous.RevokedReason);
        Assert.Equal(HashToken(refreshed.RefreshToken), previous.ReplacedByTokenHash);
        Assert.Equal("users.view", Assert.Single(refreshed.User.Permissions));
        Assert.DoesNotContain("sales.view", new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken)
            .Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value));
        Assert.Null(replacement.RevokedAt);
        Assert.NotEqual(oldRefreshToken, refreshed.RefreshToken);
        Assert.True(tokenService.ValidateToken(oldAccessToken));

        Assert.True(await auth.LogoutAsync(user.Id, refreshed.RefreshToken));
        Assert.All(await db.UserRefreshTokens.Where(token => token.UserId == user.Id).ToListAsync(),
            token =>
            {
                Assert.NotNull(token.RevokedAt);
                if (token.Id == replacement.Id)
                {
                    Assert.Equal("Logout", token.RevokedReason);
                }
            });
        Assert.True(tokenService.ValidateToken(oldAccessToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.RefreshTokenAsync(new RefreshTokenRequestDto
            {
                Token = refreshed.AccessToken,
                RefreshToken = refreshed.RefreshToken
            }));
    }

    [Fact]
    public async Task Refresh_RejectsInactiveUserButMeStillReturnsTheirPersistedProfile()
    {
        await using var db = CreateDb();
        var role = new Role { Id = 425, Name = "Cashier", Permissions = "[]" };
        var user = new User
        {
            Id = 426,
            Name = "Deactivated User",
            Email = "deactivated-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = role,
            IsActive = true
        };
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var auth = CreateAuthenticationCommandHarness(db, CreateJwtOptions());
        var login = await LoginAsync(db, CreateJwtOptions(), user.Email, Password);

        user.IsActive = false;
        await db.SaveChangesAsync();

        var me = await GetCurrentUserAsync(db, user.Id);
        Assert.NotNull(me);
        Assert.False(user.IsActive);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.RefreshTokenAsync(new RefreshTokenRequestDto
            {
                Token = login.AccessToken,
                RefreshToken = login.RefreshToken
            }));
        Assert.True(CreateTokenService(CreateJwtOptions()).ValidateToken(login.AccessToken));
    }

    [Fact]
    public async Task Refresh_RevokesExpiredRefreshTokenAndAcceptsAnExpiredAccessTokenWhenRefreshTokenIsValid()
    {
        await using var db = CreateDb();
        var role = new Role { Id = 427, Name = "Cashier", Permissions = "[]" };
        var user = new User
        {
            Id = 428,
            Name = "Expired Token User",
            Email = "expired-token-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = role,
            IsActive = true
        };
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        var options = CreateJwtOptions();
        var auth = CreateAuthenticationCommandHarness(db, options);
        var login = await LoginAsync(db, options, user.Email, Password);

        var expiredRefresh = await db.UserRefreshTokens.SingleAsync();
        expiredRefresh.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.RefreshTokenAsync(new RefreshTokenRequestDto
            {
                Token = login.AccessToken,
                RefreshToken = login.RefreshToken
            }));
        Assert.Equal("Expired", expiredRefresh.RevokedReason);
        Assert.NotNull(expiredRefresh.RevokedAt);

        var currentLogin = await LoginAsync(db, options, user.Email, Password);
        var expiredAccessToken = CreateExpiredAccessToken(user.Id);
        var refreshed = await auth.RefreshTokenAsync(new RefreshTokenRequestDto
        {
            Token = expiredAccessToken,
            RefreshToken = currentLogin.RefreshToken
        });
        Assert.Equal(user.Id, refreshed.User.Id);
    }

    [Fact]
    public async Task InvitationCompletion_HashesTokenConsumesItAndCanBeUsedOnlyOnce()
    {
        await using var db = CreateDb();
        var role = new Role { Id = 430, Name = "BusinessOwner", Permissions = """["*"]""" };
        var user = new User
        {
            Id = 431,
            Name = "Invited Owner",
            Email = "invited-owner@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("temporary-password", workFactor: 11),
            Role = role,
            BusinessId = 432,
            IsActive = false,
            MustResetPassword = true
        };
        const string invitationToken = "single-use-characterization-token";
        var invitation = new UserInvitation
        {
            User = user,
            Purpose = "owner-reset",
            TokenHash = HashToken(invitationToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(role, user, invitation);
        await db.SaveChangesAsync();
        var response = await CompleteInvitationAsync(
            db,
            CreateJwtOptions(),
            invitationToken,
            "New-Invitation-Password-123");

        Assert.Equal(user.Id, response.User.Id);
        Assert.True(user.IsActive);
        Assert.False(user.MustResetPassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("New-Invitation-Password-123", user.PasswordHash));
        Assert.NotEqual("New-Invitation-Password-123", user.PasswordHash);
        Assert.NotNull(invitation.ConsumedAt);
        Assert.Equal(HashToken(invitationToken), invitation.TokenHash);
        Assert.DoesNotContain(invitationToken, invitation.TokenHash);
        Assert.Single(await db.UserRefreshTokens.ToListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CompleteInvitationAsync(
                db,
                CreateJwtOptions(),
                invitationToken,
                "Another-Invitation-Password-123"));
    }

    [Fact]
    public async Task InvitationCompletion_RejectsExpiredInvitation()
    {
        await using var db = CreateDb();
        var role = new Role { Id = 435, Name = "BusinessOwner", Permissions = "[]" };
        var user = new User
        {
            Id = 436,
            Name = "Expired Invite",
            Email = "expired-invite@example.test",
            PasswordHash = "not-used",
            Role = role,
            IsActive = false,
            MustResetPassword = true
        };
        const string invitationToken = "expired-characterization-token";
        db.AddRange(role, user, new UserInvitation
        {
            User = user,
            Purpose = "onboarding",
            TokenHash = HashToken(invitationToken),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CompleteInvitationAsync(
                db,
                CreateJwtOptions(),
                invitationToken,
                "New-Invitation-Password-123"));

        Assert.False(user.IsActive);
        Assert.True(user.MustResetPassword);
        Assert.Empty(await db.UserRefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task RoleSwitch_UsesPersistedBusinessOwnerAndRefreshKeepsAuthorizedContext()
    {
        using var factory = new RetailPosApiFactory();
        var ownerRole = new Role
        {
            Id = 440,
            Name = RoleSwitchClaims.BusinessOwnerRoleName,
            Permissions = """["businesses.view"]"""
        };
        var actingRole = new Role { Id = 441, Name = "Cashier", Permissions = """["pos.view"]""" };
        var business = new Business { Id = 442, Name = "Switch Business", IsActive = true };
        var outlet = new Outlet { Id = 443, BusinessId = business.Id, Name = "Unassigned Outlet" };
        var foreignBusiness = new Business { Id = 445, Name = "Other Switch Business", IsActive = true };
        var foreignOutlet = new Outlet { Id = 446, BusinessId = foreignBusiness.Id, Name = "Foreign Outlet" };
        var user = new User
        {
            Id = 444,
            BusinessId = business.Id,
            Name = "Switch Owner",
            Email = "switch-owner@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = ownerRole,
            IsActive = true
        };
        await SeedApiDbAsync(factory, ownerRole, actingRole, business, outlet, foreignBusiness, foreignOutlet, user);

        using var client = CreateTestClient(factory, user.Id, ownerRole.Name);
        using var deniedOutlet = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingOutletId = foreignOutlet.Id
        });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, deniedOutlet.StatusCode);

        using var switchResponse = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingOutletId = outlet.Id
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, switchResponse.StatusCode);
        var switched = await switchResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(switched);

        Assert.Equal(RoleSwitchClaims.BusinessOwnerRoleName, switched!.User.RealRoleName);
        Assert.Equal("Cashier", switched.User.RoleName);
        Assert.Equal("Cashier", switched.User.ActingRoleName);
        Assert.Equal(outlet.Id, switched.User.ActingOutletId);
        Assert.True(switched.User.IsRoleSwitched);
        Assert.DoesNotContain("businesses.view", switched.User.Permissions);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(switched.Token).Claims.ToArray();
        Assert.Contains(claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Cashier");
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.RealRoleName
            && claim.Value == RoleSwitchClaims.BusinessOwnerRoleName);
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.ActingRoleId
            && claim.Value == actingRole.Id.ToString());
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.ActingOutletId
            && claim.Value == outlet.Id.ToString());
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.IsRoleSwitched && claim.Value == "true");

        using var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<UserInfoDto>();
        Assert.NotNull(me);
        Assert.Equal(RoleSwitchClaims.BusinessOwnerRoleName, me!.RoleName);
        Assert.Equal(RoleSwitchClaims.BusinessOwnerRoleName, me.RealRoleName);

        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequestDto
        {
            Token = switched.Token,
            RefreshToken = switched.RefreshToken
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(refreshed);
        Assert.Equal("Cashier", refreshed!.User.RoleName);
        Assert.Equal(RoleSwitchClaims.BusinessOwnerRoleName, refreshed.User.RealRoleName);
        Assert.True(refreshed.User.IsRoleSwitched);
        Assert.Equal(outlet.Id, refreshed.User.ActingOutletId);
        Assert.Contains("acting_role", new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Token)
            .Claims.Select(claim => claim.Type));

        using var returnOwnerResponse = await client.PostAsync("/api/auth/return-owner", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, returnOwnerResponse.StatusCode);
        var returned = await returnOwnerResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(returned);
        Assert.Equal(RoleSwitchClaims.BusinessOwnerRoleName, returned!.User.RoleName);
        Assert.Null(returned.User.ActingRoleName);
        Assert.False(returned.User.IsRoleSwitched);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(returned.Token).Claims,
            claim => claim.Type == ClaimTypes.Role && claim.Value == RoleSwitchClaims.BusinessOwnerRoleName);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvents = await db.AuditEvents.ToListAsync();
        Assert.NotEmpty(auditEvents);
        Assert.All(auditEvents, auditEvent =>
        {
            Assert.DoesNotContain(switched.RefreshToken, auditEvent.ActionSummary);
            Assert.DoesNotContain(returned.RefreshToken, auditEvent.ActionSummary);
            Assert.DoesNotContain(switched.Token, auditEvent.ActionSummary);
            Assert.DoesNotContain(returned.Token, auditEvent.ActionSummary);
            Assert.DoesNotContain(switched.RefreshToken, auditEvent.ErrorMessage ?? string.Empty);
            Assert.DoesNotContain(returned.RefreshToken, auditEvent.ErrorMessage ?? string.Empty);
            Assert.DoesNotContain(switched.RefreshToken, auditEvent.Metadata ?? string.Empty);
            Assert.DoesNotContain(returned.RefreshToken, auditEvent.Metadata ?? string.Empty);
        });
    }

    [Fact]
    public void AuthenticationInvitationPasswordAndOnboardingRoutesRemainInOpenApi()
    {
        using var factory = new RetailPosApiFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        AssertRoute(swagger, "/api/Auth/login", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/register", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/complete-invitation", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/refresh-token", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/logout", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/logout-all", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/switch-role", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/return-owner", OperationType.Post);
        AssertRoute(swagger, "/api/Auth/me", OperationType.Get);
        AssertRoute(swagger, "/api/businesses", OperationType.Post);
        AssertRoute(swagger, "/api/businesses/{businessId}/owner/reset-access", OperationType.Post);
        AssertRoute(swagger, "/api/users/{id}/change-password", OperationType.Post);
        AssertRoute(swagger, "/api/users/my-password", OperationType.Post);
    }

    [Fact]
    public async Task RegisterEndpoint_RequiresPersistedRealSuperAdminAndPreservesResponse()
    {
        using var factory = new RetailPosApiFactory();
        var superAdminRole = new Role { Id = 450, Name = RoleSwitchClaims.SuperAdminRoleName, Permissions = """["*"]""" };
        var ordinaryRole = new Role { Id = 451, Name = "AccountsAdmin", Permissions = "[]" };
        var registrationRole = new Role { Id = 452, Name = "Cashier", Permissions = """["pos.view"]""" };
        var superAdmin = new User
        {
            Id = 453,
            Name = "Real Super Admin",
            Email = "real-super-admin@example.test",
            PasswordHash = "not-used",
            Role = superAdminRole,
            IsActive = true
        };
        var ordinaryUser = new User
        {
            Id = 454,
            Name = "Ordinary Actor",
            Email = "ordinary-actor@example.test",
            PasswordHash = "not-used",
            Role = ordinaryRole,
            IsActive = true
        };
        await SeedApiDbAsync(factory, superAdminRole, ordinaryRole, registrationRole, superAdmin, ordinaryUser);

        using var authorizedClient = CreateTestClient(factory, superAdmin.Id, RoleSwitchClaims.SuperAdminRoleName);
        using var response = await authorizedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            Name = "Registration User",
            Email = "registration-user@example.test",
            Password = Password,
            ConfirmPassword = Password
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginResponse);
        Assert.Equal("Registration User", loginResponse!.User.Name);
        Assert.Equal("Cashier", loginResponse.User.RoleName);
        Assert.Equal(64, Convert.FromBase64String(loginResponse.RefreshToken).Length);

        using var spoofedClient = CreateTestClient(
            factory,
            ordinaryUser.Id,
            ordinaryRole.Name,
            effectiveRole: RoleSwitchClaims.SuperAdminRoleName,
            isRoleSwitched: true);
        using var spoofedResponse = await spoofedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            Name = "Spoofed Registration",
            Email = "spoofed-registration@example.test",
            Password = Password,
            ConfirmPassword = Password
        });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, spoofedResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(user => user.Email == "registration-user@example.test"));
        Assert.Empty(await db.Users.Where(user => user.Email == "spoofed-registration@example.test").ToListAsync());
    }

    [Fact]
    public async Task CompleteInvitationEndpoint_ConsumesTokenAndReturnsExistingCredentialDto()
    {
        using var factory = new RetailPosApiFactory();
        const string invitationToken = "http-invitation-completion-token";
        var role = new Role { Id = 460, Name = "BusinessOwner", Permissions = """["businesses.view"]""" };
        var user = new User
        {
            Id = 461,
            Name = "HTTP Invited Owner",
            Email = "http-invited-owner@example.test",
            PasswordHash = "temporary",
            Role = role,
            BusinessId = 462,
            IsActive = false,
            MustResetPassword = true
        };
        var invitation = new UserInvitation
        {
            Id = 463,
            User = user,
            Purpose = "onboarding",
            TokenHash = HashToken(invitationToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        await SeedApiDbAsync(factory, role, user, invitation);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/complete-invitation", new CompleteInvitationRequestDto
        {
            Token = invitationToken,
            NewPassword = "HTTP-Completed-Password-123"
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginResponse);
        Assert.Equal(user.Id, loginResponse!.User.Id);
        Assert.Equal("HTTP Invited Owner", loginResponse.User.Name);
        Assert.NotEmpty(loginResponse.Token);
        Assert.NotEmpty(loginResponse.RefreshToken);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.True(persisted.IsActive);
        Assert.False(persisted.MustResetPassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("HTTP-Completed-Password-123", persisted.PasswordHash));
        Assert.NotNull((await db.UserInvitations.SingleAsync(candidate => candidate.Id == invitation.Id)).ConsumedAt);
        Assert.Equal(HashToken(invitationToken), invitation.TokenHash);
    }

    [Fact]
    public async Task CurrentUserEndpoint_ReturnsPersistedProfileForInactiveUserAndIgnoresSwitchedRole()
    {
        using var factory = new RetailPosApiFactory();
        var role = new Role
        {
            Id = 465,
            Name = "Cashier",
            Permissions = """["users.view"]"""
        };
        var business = new Business { Id = 466, Name = "Current User Business", IsActive = true };
        var outlet = new Outlet { Id = 467, BusinessId = business.Id, Name = "Main Outlet" };
        var user = new User
        {
            Id = 468,
            BusinessId = business.Id,
            Name = "Inactive Profile",
            Email = "inactive-profile@example.test",
            PasswordHash = "must-not-be-projected",
            Role = role,
            Outlet = outlet,
            PermissionOverrides = """["users.edit","users.view","pos.sale.backdate"]""",
            IsActive = false,
            MustResetPassword = true
        };
        await SeedApiDbAsync(factory, business, outlet, role, user);

        using var client = CreateTestClient(
            factory,
            user.Id,
            role: "Cashier",
            effectiveRole: "BusinessOwner",
            isRoleSwitched: true,
            permissions: "businesses.view");
        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserInfoDto>();
        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile!.Id);
        Assert.Equal(user.BusinessId, profile.BusinessId);
        Assert.Equal(user.Name, profile.Name);
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal("Cashier", profile.RoleName);
        Assert.Equal("Cashier", profile.RealRoleName);
        Assert.Equal(outlet.Id, profile.OutletId);
        Assert.Equal("Main Outlet", profile.OutletName);
        Assert.True(profile.MustResetPassword);
        Assert.False(profile.IsBusinessOwner);
        Assert.Null(profile.ActingRoleName);
        Assert.False(profile.IsRoleSwitched);
        Assert.Contains("users.edit", profile.Permissions);
        Assert.Contains("sales.backdate", profile.Permissions);
        Assert.DoesNotContain("businesses.view", profile.Permissions);

        using var missingUserClient = CreateTestClient(factory, user.Id + 1000, role: "Cashier");
        using var missingResponse = await missingUserClient.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task LogoutEndpoints_RevokeOwnedSessionAndKeepAllDeviceLogoutSeparateWithoutAuditingSecrets()
    {
        using var factory = new RetailPosApiFactory();
        const string currentSessionToken = "api-current-session-refresh-secret";
        const string otherSessionToken = "api-other-session-refresh-secret";
        const string otherUserToken = "api-other-user-refresh-secret";
        var role = new Role { Id = 480, Name = "Logout User Role", Permissions = "[]" };
        var otherRole = new Role { Id = 481, Name = "Other Logout User Role", Permissions = "[]" };
        var user = new User
        {
            Id = 482,
            Name = "Logout User",
            Email = "logout-user@example.test",
            PasswordHash = "not-used",
            Role = role,
            IsActive = true
        };
        var otherUser = new User
        {
            Id = 483,
            Name = "Other Logout User",
            Email = "other-logout-user@example.test",
            PasswordHash = "not-used",
            Role = otherRole,
            IsActive = true
        };
        await SeedApiDbAsync(
            factory,
            role,
            otherRole,
            user,
            otherUser,
            new UserRefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(currentSessionToken),
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            },
            new UserRefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(otherSessionToken),
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            },
            new UserRefreshToken
            {
                UserId = otherUser.Id,
                TokenHash = HashToken(otherUserToken),
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });

        using var client = CreateTestClient(factory, user.Id, role.Name);
        var request = new LogoutRequestDto { RefreshToken = currentSessionToken };
        using var firstLogout = await client.PostAsJsonAsync("/api/auth/logout", request);
        using var repeatedLogout = await client.PostAsJsonAsync("/api/auth/logout", request);
        using var wrongOwnerLogout = await client.PostAsJsonAsync(
            "/api/auth/logout",
            new LogoutRequestDto { RefreshToken = otherUserToken });

        Assert.Equal(System.Net.HttpStatusCode.OK, firstLogout.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, repeatedLogout.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, wrongOwnerLogout.StatusCode);
        Assert.DoesNotContain(currentSessionToken, await firstLogout.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.NotNull((await db.UserRefreshTokens.SingleAsync(token =>
                token.TokenHash == HashToken(currentSessionToken))).RevokedAt);
            Assert.Null((await db.UserRefreshTokens.SingleAsync(token =>
                token.TokenHash == HashToken(otherSessionToken))).RevokedAt);
            Assert.Null((await db.UserRefreshTokens.SingleAsync(token =>
                token.TokenHash == HashToken(otherUserToken))).RevokedAt);

            var auditEvents = await db.AuditEvents.ToListAsync();
            Assert.NotEmpty(auditEvents);
            Assert.All(auditEvents, auditEvent =>
            {
                Assert.DoesNotContain(currentSessionToken, auditEvent.ActionSummary);
                Assert.DoesNotContain(otherUserToken, auditEvent.ActionSummary);
                Assert.DoesNotContain(currentSessionToken, auditEvent.ErrorMessage ?? string.Empty);
                Assert.DoesNotContain(otherUserToken, auditEvent.ErrorMessage ?? string.Empty);
                Assert.DoesNotContain(currentSessionToken, auditEvent.Metadata ?? string.Empty);
                Assert.DoesNotContain(otherUserToken, auditEvent.Metadata ?? string.Empty);
            });
        }

        using var allSessionsLogout = await client.PostAsync("/api/auth/logout-all", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, allSessionsLogout.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.NotNull((await verifyDb.UserRefreshTokens.SingleAsync(token =>
            token.TokenHash == HashToken(otherSessionToken))).RevokedAt);
        Assert.Null((await verifyDb.UserRefreshTokens.SingleAsync(token =>
            token.TokenHash == HashToken(otherUserToken))).RevokedAt);
    }

    [Fact]
    public async Task PasswordChangeEndpoints_PreserveScopeAndStatusBehavior()
    {
        using var factory = new RetailPosApiFactory();
        var adminRole = new Role { Id = 470, Name = "AccountsAdmin", Permissions = "[]" };
        var userRole = new Role { Id = 471, Name = "Cashier", Permissions = "[]" };
        var protectedRole = new Role { Id = 472, Name = RoleSwitchClaims.SuperAdminRoleName, Permissions = "[]" };
        var admin = new User
        {
            Id = 473,
            BusinessId = 474,
            Name = "Password Admin",
            Email = "password-admin@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = adminRole,
            IsActive = true
        };
        var target = new User
        {
            Id = 475,
            BusinessId = admin.BusinessId,
            Name = "Password Target",
            Email = "password-target@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = userRole,
            IsActive = true
        };
        var protectedTarget = new User
        {
            Id = 476,
            BusinessId = admin.BusinessId,
            Name = "Protected Admin",
            Email = "protected-admin@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = protectedRole,
            IsActive = true
        };
        var otherBusinessTarget = new User
        {
            Id = 477,
            BusinessId = 478,
            Name = "Other Business User",
            Email = "other-business-user@example.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 11),
            Role = userRole,
            IsActive = true
        };
        await SeedApiDbAsync(
            factory,
            adminRole,
            userRole,
            protectedRole,
            admin,
            target,
            protectedTarget,
            otherBusinessTarget);
        using var client = CreateTestClient(
            factory,
            admin.Id,
            adminRole.Name,
            permissions: "users.view,users.edit");
        var change = new RetailPOS.API.DTOs.Users.ChangePasswordDto
        {
            CurrentPassword = Password,
            NewPassword = "New-Target-Password-123",
            ConfirmPassword = "New-Target-Password-123"
        };

        using var adminChange = await client.PostAsJsonAsync($"/api/users/{target.Id}/change-password", change);
        Assert.Equal(System.Net.HttpStatusCode.OK, adminChange.StatusCode);
        using var protectedChange = await client.PostAsJsonAsync(
            $"/api/users/{protectedTarget.Id}/change-password",
            change);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, protectedChange.StatusCode);
        using var crossBusinessChange = await client.PostAsJsonAsync(
            $"/api/users/{otherBusinessTarget.Id}/change-password",
            change);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, crossBusinessChange.StatusCode);
        using var wrongCurrentPassword = await client.PostAsJsonAsync(
            $"/api/users/{target.Id}/change-password",
            new RetailPOS.API.DTOs.Users.ChangePasswordDto
            {
                CurrentPassword = Password,
                NewPassword = "Another-Target-Password-123",
                ConfirmPassword = "Another-Target-Password-123"
            });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, wrongCurrentPassword.StatusCode);

        using var selfChange = await client.PostAsJsonAsync(
            "/api/users/my-password",
            new RetailPOS.API.DTOs.Users.ChangePasswordDto
            {
                CurrentPassword = Password,
                NewPassword = "New-Admin-Password-123",
                ConfirmPassword = "New-Admin-Password-123"
            });
        Assert.Equal(System.Net.HttpStatusCode.OK, selfChange.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.True(BCrypt.Net.BCrypt.Verify("New-Target-Password-123",
            (await db.Users.SingleAsync(user => user.Id == target.Id)).PasswordHash));
        Assert.True(BCrypt.Net.BCrypt.Verify("New-Admin-Password-123",
            (await db.Users.SingleAsync(user => user.Id == admin.Id)).PasswordHash));
        Assert.True(BCrypt.Net.BCrypt.Verify(Password,
            (await db.Users.SingleAsync(user => user.Id == protectedTarget.Id)).PasswordHash));
    }

    private static HttpClient CreateTestClient(
        RetailPosApiFactory factory,
        long userId,
        string role,
        string? effectiveRole = null,
        bool isRoleSwitched = false,
        string permissions = "accounts.view")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (effectiveRole is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", effectiveRole);
        }
        if (isRoleSwitched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        }
        return client;
    }

    private static async Task SeedApiDbAsync(RetailPosApiFactory factory, params object[] entities)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static void AssertRoute(OpenApiDocument document, string path, OperationType operation)
    {
        var route = document.Paths.SingleOrDefault(item =>
            string.Equals(item.Key, path, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(operation, route.Value!.Operations.Keys);
    }

    private static RetailPOSDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"AuthenticationCharacterization_{Guid.NewGuid()}")
            .Options);

    private static IOptions<JwtSettings> CreateJwtOptions(int expirationMinutes = 60, int refreshDays = 7) =>
        Options.Create(new JwtSettings
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            ExpirationMinutes = expirationMinutes,
            RefreshTokenExpirationDays = refreshDays
        });

    private static AuthenticationCommandTestHarness CreateAuthenticationCommandHarness(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options) =>
        new(db, options);

    private static TokenService CreateTokenService(IOptions<JwtSettings> options) =>
        new(new JwtAuthenticationTokenService(CreateTokenSettings(options)));

    private static IOptions<JwtTokenSettings> CreateTokenSettings(IOptions<JwtSettings> options) =>
        Options.Create(new JwtTokenSettings
        {
            Secret = options.Value.Secret,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            ExpirationMinutes = options.Value.ExpirationMinutes,
            RefreshTokenExpirationDays = options.Value.RefreshTokenExpirationDays
        });

    private static Task<AuthenticationUserProfile?> GetCurrentUserAsync(RetailPOSDbContext db, long userId) =>
        new GetCurrentUserQueryHandler(new AuthenticationStore(db))
            .Handle(new GetCurrentUserQuery(userId), CancellationToken.None);

    private static Task<AuthenticationResult> LoginAsync(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options,
        string email,
        string password,
        bool rememberMe = false) =>
        new LoginCommandHandler(
            new AuthenticationStore(db),
            new BCryptPasswordHasher(),
            new AuthenticationCredentialsIssuer(
                new JwtAuthenticationTokenService(CreateTokenSettings(options)),
                CreateTokenSettings(options)))
            .Handle(new LoginCommand(email, password, rememberMe), CancellationToken.None);

    private static Task<AuthenticationResult> CompleteInvitationAsync(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options,
        string token,
        string password) =>
        new CompleteInvitationCommandHandler(
            new AuthenticationStore(db),
            new BCryptPasswordHasher(),
            new AuthenticationCredentialsIssuer(
                new JwtAuthenticationTokenService(CreateTokenSettings(options)),
                CreateTokenSettings(options)))
            .Handle(new CompleteInvitationCommand(token, password), CancellationToken.None);

    private static string CreateExpiredAccessToken(long userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-20),
            expires: DateTime.UtcNow.AddMinutes(-10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
