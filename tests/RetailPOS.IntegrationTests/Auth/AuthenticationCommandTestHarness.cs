using Microsoft.Extensions.Options;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Auth;

internal sealed class AuthenticationCommandTestHarness
{
    private readonly RetailPOSDbContext _db;
    private readonly IAuthenticationTokenService _tokenService;
    private readonly IAuthenticationCredentialsIssuer _credentialsIssuer;
    private readonly IRefreshTokenStore _refreshTokenStore;

    public AuthenticationCommandTestHarness(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options,
        IRefreshTokenStore? refreshTokenStore = null)
    {
        _db = db;
        var tokenSettings = Options.Create(new JwtTokenSettings
        {
            Secret = options.Value.Secret,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            ExpirationMinutes = options.Value.ExpirationMinutes,
            RefreshTokenExpirationDays = options.Value.RefreshTokenExpirationDays
        });
        _tokenService = new JwtAuthenticationTokenService(tokenSettings);
        _credentialsIssuer = new AuthenticationCredentialsIssuer(_tokenService, tokenSettings);
        _refreshTokenStore = refreshTokenStore ?? new RefreshTokenStore(db);
    }

    public Task<AuthenticationResult> RefreshTokenAsync(
        RefreshTokenRequestDto request,
        CancellationToken cancellationToken = default) =>
        new RefreshTokenCommandHandler(
                _tokenService,
                new AuthenticationStore(_db),
                _credentialsIssuer,
                _refreshTokenStore,
                new UserContextPolicy(new AuthenticationStore(_db), new UserOutletAccessStore(_db)))
            .Handle(
                new RefreshTokenCommand(request.Token, request.RefreshToken),
                cancellationToken);

    public Task<bool> LogoutAsync(
        long userId,
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        new LogoutCommandHandler(_refreshTokenStore)
            .Handle(new LogoutCommand(userId, refreshToken), cancellationToken);

    public Task<bool> LogoutAllAsync(
        long userId,
        CancellationToken cancellationToken = default) =>
        new LogoutAllCommandHandler(_refreshTokenStore)
            .Handle(new LogoutAllCommand(userId), cancellationToken);
}
