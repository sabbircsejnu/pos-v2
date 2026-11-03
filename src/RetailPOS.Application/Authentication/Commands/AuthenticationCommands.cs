using System.Text.Json;
using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Commands;

public sealed class LoginCommand : ICommand<AuthenticationResult>
{
    public LoginCommand(string email, string password, bool rememberMe)
    {
        Email = email;
        Password = password;
        RememberMe = rememberMe;
    }

    public string Email { get; }
    public string Password { get; }
    public bool RememberMe { get; }
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty().MinimumLength(6);
    }
}

public sealed class LoginCommandHandler(
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    IAuthenticationCredentialsIssuer credentialsIssuer)
    : IRequestHandler<LoginCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var user = await store.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (user.MustResetPassword)
        {
            throw new UnauthorizedAccessException(
                "Password setup is required. Complete your invitation before signing in.");
        }

        var permissions = BuildEffectivePermissions(user);
        var credentials = credentialsIssuer.Issue(user, permissions);
        await store.PersistRefreshTokenAsync(
            user.Id,
            credentials.RefreshToken,
            credentials.RefreshTokenExpiresAt,
            cancellationToken);

        return CreateResult(credentials, user, permissions);
    }

    internal static List<string> BuildEffectivePermissions(AuthenticationUser user)
        => BuildEffectivePermissions(user.RolePermissions, user.PermissionOverrides);

    internal static List<string> BuildEffectivePermissions(
        string? rolePermissions,
        string? permissionOverrides)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in ParsePermissionJson(rolePermissions))
        {
            merged.Add(permission);
        }

        foreach (var permission in ParsePermissionJson(permissionOverrides))
        {
            if (string.Equals(permission, "pos.sale.backdate", StringComparison.OrdinalIgnoreCase))
            {
                merged.Add("sales.backdate");
            }
            else
            {
                merged.Add(permission);
            }
        }

        return merged.ToList();
    }

    internal static IReadOnlyList<string> ParsePermissionJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    internal static AuthenticationResult CreateResult(
        AuthenticationCredentials credentials,
        AuthenticationUser user,
        IReadOnlyList<string> permissions) =>
        new(
            credentials.AccessToken,
            credentials.RefreshToken,
            credentials.AccessTokenExpiresAt,
            new AuthenticationUserProfile(
                user.Id,
                user.BusinessId,
                user.Name,
                user.Email,
                user.RoleName,
                permissions,
                user.OutletId,
                user.OutletName,
                user.RoleName,
                string.Equals(
                    user.RoleName,
                    SystemRoleNames.BusinessOwner,
                    StringComparison.OrdinalIgnoreCase),
                user.MustResetPassword));
}

public sealed class RegisterCommand : ICommand<AuthenticationResult>
{
    public RegisterCommand(string name, string email, string password, string confirmPassword)
    {
        Name = name;
        Email = email;
        Password = password;
        ConfirmPassword = confirmPassword;
    }

    public string Name { get; }
    public string Email { get; }
    public string Password { get; }
    public string ConfirmPassword { get; }
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MinimumLength(2).MaximumLength(255);
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty().MinimumLength(6);
        RuleFor(command => command.ConfirmPassword)
            .Equal(command => command.Password)
            .WithMessage("Passwords do not match");
    }
}

public sealed class RegisterCommandHandler(
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    IAuthenticationCredentialsIssuer credentialsIssuer,
    IUserGovernanceStore governanceStore,
    IUserGovernanceContext governanceContext)
    : IRequestHandler<RegisterCommand, AuthenticationResult>
{
    public Task<AuthenticationResult> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken) =>
        store.ExecuteAtomicallyAsync(async transactionToken =>
        {
            if (governanceContext.RealUserId is not long actorId)
            {
                throw new UnauthorizedAccessException("Only a real Super Admin may register users.");
            }

            var actor = await governanceStore.FindActorAsync(actorId, transactionToken);
            if (actor is null || !actor.IsActive
                || !UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName))
            {
                throw new UnauthorizedAccessException("Only a real Super Admin may register users.");
            }

            if (await store.EmailExistsAsync(request.Email, transactionToken))
            {
                throw new InvalidOperationException("Email already registered");
            }

            var role = await store.FindRegistrationRoleAsync(transactionToken)
                ?? throw new InvalidOperationException("Default registration role is not configured.");

            if (string.Equals(role.Name, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase)
                || string.Equals(role.Name, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Registration role configuration is invalid.");
            }

            var passwordHash = passwordHasher.Hash(request.Password);
            var user = await store.CreateRegisteredUserAsync(
                request.Name,
                request.Email,
                passwordHash,
                role.Id,
                transactionToken);

            var permissions = LoginCommandHandler.BuildEffectivePermissions(user);
            var credentials = credentialsIssuer.Issue(user, permissions);
            await store.PersistRefreshTokenAsync(
                user.Id,
                credentials.RefreshToken,
                credentials.RefreshTokenExpiresAt,
                transactionToken);

            return LoginCommandHandler.CreateResult(credentials, user, permissions);
        }, cancellationToken);
}

public sealed class CompleteInvitationCommand : ICommand<AuthenticationResult>
{
    public CompleteInvitationCommand(string token, string newPassword)
    {
        Token = token;
        NewPassword = newPassword;
    }

    public string Token { get; }
    public string NewPassword { get; }
}

public sealed class CompleteInvitationCommandValidator : AbstractValidator<CompleteInvitationCommand>
{
    public CompleteInvitationCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithMessage("Invitation token is required.");
        RuleFor(command => command.NewPassword)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("New password must be at least 8 characters.");
    }
}

public sealed class CompleteInvitationCommandHandler(
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    IAuthenticationCredentialsIssuer credentialsIssuer)
    : IRequestHandler<CompleteInvitationCommand, AuthenticationResult>
{
    public Task<AuthenticationResult> Handle(
        CompleteInvitationCommand request,
        CancellationToken cancellationToken) =>
        store.ExecuteAtomicallyAsync(async transactionToken =>
        {
            var invitation = await store.FindInvitationAsync(request.Token, transactionToken);
            if (invitation is null || invitation.ConsumedAt.HasValue || invitation.ExpiresAt < DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Invitation token is invalid or expired.");
            }

            var now = DateTime.UtcNow;
            var user = await store.CompleteInvitationAsync(
                invitation.Id,
                passwordHasher.Hash(request.NewPassword, workFactor: 11),
                now,
                transactionToken);
            if (user is null)
            {
                throw new UnauthorizedAccessException("Invitation token is invalid or expired.");
            }

            var permissions = LoginCommandHandler.BuildEffectivePermissions(user);
            var credentials = credentialsIssuer.Issue(user, permissions);
            await store.PersistRefreshTokenAsync(
                user.Id,
                credentials.RefreshToken,
                credentials.RefreshTokenExpiresAt,
                transactionToken);

            return LoginCommandHandler.CreateResult(credentials, user, permissions);
        }, cancellationToken);
}

public sealed class ChangeUserPasswordCommand : ICommand<bool>
{
    public ChangeUserPasswordCommand(
        long userId,
        string currentPassword,
        string newPassword,
        string confirmPassword)
    {
        UserId = userId;
        CurrentPassword = currentPassword;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
    }

    public long UserId { get; }
    public string CurrentPassword { get; }
    public string NewPassword { get; }
    public string ConfirmPassword { get; }
}

public sealed class ChangeMyPasswordCommand : ICommand<bool>
{
    public ChangeMyPasswordCommand(string currentPassword, string newPassword, string confirmPassword)
    {
        CurrentPassword = currentPassword;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
    }

    public string CurrentPassword { get; }
    public string NewPassword { get; }
    public string ConfirmPassword { get; }
}

public sealed class ChangeUserPasswordCommandValidator : AbstractValidator<ChangeUserPasswordCommand>
{
    public ChangeUserPasswordCommandValidator()
    {
        RuleFor(command => command.UserId).GreaterThan(0);
        RuleFor(command => command.CurrentPassword).NotEmpty();
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(6).MaximumLength(100);
        RuleFor(command => command.ConfirmPassword)
            .Equal(command => command.NewPassword)
            .WithMessage("Passwords do not match");
    }
}

public sealed class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public ChangeMyPasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty();
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(6).MaximumLength(100);
        RuleFor(command => command.ConfirmPassword)
            .Equal(command => command.NewPassword)
            .WithMessage("Passwords do not match");
    }
}

public sealed class ChangeUserPasswordCommandHandler(
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    IUserGovernanceStore governanceStore,
    IUserGovernanceContext governanceContext)
    : IRequestHandler<ChangeUserPasswordCommand, bool>
{
    public Task<bool> Handle(
        ChangeUserPasswordCommand request,
        CancellationToken cancellationToken) =>
        ChangeAsync(request.UserId, request.CurrentPassword, request.NewPassword, cancellationToken);

    internal Task<bool> ChangeAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken) =>
        store.ExecuteAtomicallyAsync(async transactionToken =>
        {
            var actor = await GetActorAsync(governanceStore, governanceContext, transactionToken);
            var isSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName);
            var businessId = isSuperAdmin ? null : actor.BusinessId;
            if (!isSuperAdmin && !businessId.HasValue)
            {
                throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();
            }

            var target = await governanceStore.FindTargetAsync(userId, businessId, transactionToken)
                ?? throw new KeyNotFoundException($"User with ID {userId} not found");
            if (UserGovernanceCommandHandlerHelpers.IsSuperAdmin(target.RoleName) && !isSuperAdmin)
            {
                throw new ProtectedUserPasswordChangeException();
            }

            var user = await store.FindUserForPasswordChangeAsync(userId, businessId, transactionToken)
                ?? throw new KeyNotFoundException($"User with ID {userId} not found");
            await ChangePasswordAsync(user, currentPassword, newPassword, transactionToken);
            return true;
        }, cancellationToken);

    internal static async Task<UserGovernanceActor> GetActorAsync(
        IUserGovernanceStore store,
        IUserGovernanceContext context,
        CancellationToken cancellationToken)
    {
        if (context.RealUserId is not long userId)
        {
            throw new UnauthorizedAccessException("Unable to determine the authenticated user.");
        }

        return await store.FindActorAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The authenticated user is not a persisted account.");
    }

    internal async Task ChangePasswordAsync(
        AuthenticationUser user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Current password is incorrect");
        }

        await store.UpdatePasswordAsync(
            user.Id,
            passwordHasher.Hash(newPassword, workFactor: 11),
            DateTime.UtcNow,
            cancellationToken);
    }
}

public sealed class ChangeMyPasswordCommandHandler(
    ChangeUserPasswordCommandHandler changePassword,
    IUserGovernanceContext governanceContext)
    : IRequestHandler<ChangeMyPasswordCommand, bool>
{
    public async Task<bool> Handle(
        ChangeMyPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (governanceContext.RealUserId is not long userId)
        {
            throw new UnauthorizedAccessException("Unable to determine the authenticated user.");
        }

        return await changePassword.ChangeAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
    }
}

public sealed class ProtectedUserPasswordChangeException()
    : UnauthorizedAccessException("Only a real Super Admin can change a Super Admin user's password.");
