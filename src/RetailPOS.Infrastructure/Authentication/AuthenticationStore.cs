using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Authentication;

public sealed class AuthenticationStore(RetailPOSDbContext dbContext) : IAuthenticationStore
{
    public async Task<TResult> ExecuteAtomicallyAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AuthenticationUser?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(user => user.Role)
            .Include(user => user.Outlet)
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        return user is null ? null : Map(user);
    }

    public Task<AuthenticationProfileData?> FindProfileAsync(
        long userId,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AuthenticationProfileData(
                user.Id,
                user.BusinessId,
                user.Name,
                user.Email,
                user.RoleId,
                user.Role == null ? null : user.Role.Name,
                user.Role == null ? null : user.Role.Permissions,
                user.OutletId,
                user.Outlet == null ? null : user.Outlet.Name,
                user.PermissionOverrides,
                user.MustResetPassword,
                user.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AuthenticationRole?> FindRegistrationRoleAsync(CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .FirstOrDefaultAsync(candidate => candidate.Name == "Cashier", cancellationToken)
            ?? await dbContext.Roles
                .FirstOrDefaultAsync(candidate => candidate.Name == "User", cancellationToken);

        return role is null ? null : new AuthenticationRole(role.Id, role.Name, role.Permissions);
    }

    public Task<AuthenticationRoleSwitchUser?> FindActiveUserForRoleSwitchAsync(
        long userId,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new AuthenticationRoleSwitchUser(
                user.Id,
                user.BusinessId,
                user.Name,
                user.Email,
                user.RoleId,
                user.Role == null ? null : user.Role.Name,
                user.Role == null ? null : user.Role.Permissions,
                user.OutletId,
                user.Outlet == null ? null : user.Outlet.Name,
                user.IsActive,
                user.MustResetPassword,
                user.PermissionOverrides,
                user.ContextSwitchEnabled,
                user.ContextLocationScope,
                user.ContextRoleScope))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AuthenticationRole>> ListBusinessContextRolesAsync(
        long businessId,
        IReadOnlyCollection<string> defaultRoleNames,
        CancellationToken cancellationToken)
    {
        var defaultNames = defaultRoleNames.Select(name => name.ToLower()).ToList();
        return await dbContext.Roles
            .AsNoTracking()
            .Where(role => defaultNames.Contains(role.Name.ToLower())
                || role.Users.Any(user => user.BusinessId == businessId))
            .Select(role => new AuthenticationRole(role.Id, role.Name, role.Permissions))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuthenticationRole>> ListMappedContextRolesAsync(
        long userId,
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.UserContextRoles
            .AsNoTracking()
            .Where(mapping => mapping.UserId == userId && mapping.BusinessId == businessId)
            .Select(mapping => new AuthenticationRole(mapping.Role!.Id, mapping.Role.Name, mapping.Role.Permissions))
            .ToListAsync(cancellationToken);

    public async Task<AuthenticationRole?> FindRoleForSwitchAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        var normalizedName = roleName.ToLower();
        var role = await dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Name.ToLower() == normalizedName, cancellationToken);
        return role is null ? null : new AuthenticationRole(role.Id, role.Name, role.Permissions);
    }

    public Task<AuthenticationOutlet?> FindOutletForSwitchAsync(
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == outletId)
            .Select(outlet => new AuthenticationOutlet(outlet.Id, outlet.BusinessId, outlet.Name))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public async Task<AuthenticationUser> CreateRegisteredUserAsync(
        string name,
        string email,
        string passwordHash,
        long roleId,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.Entry(user).Reference(candidate => candidate.Role).LoadAsync(cancellationToken);
        await dbContext.Entry(user).Reference(candidate => candidate.Outlet).LoadAsync(cancellationToken);
        return Map(user);
    }

    public async Task<AuthenticationInvitation?> FindInvitationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(token.Trim());
        var invitation = await dbContext.UserInvitations
            .AsNoTracking()
            .Include(candidate => candidate.User)
                .ThenInclude(user => user.Role)
            .Include(candidate => candidate.User)
                .ThenInclude(user => user.Outlet)
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

        return invitation is null
            ? null
            : new AuthenticationInvitation(
                invitation.Id,
                invitation.Purpose,
                invitation.ExpiresAt,
                invitation.ConsumedAt,
                Map(invitation.User));
    }

    public async Task<AuthenticationUser?> CompleteInvitationAsync(
        long invitationId,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.UserInvitations
            .Include(candidate => candidate.User)
                .ThenInclude(user => user.Role)
            .Include(candidate => candidate.User)
                .ThenInclude(user => user.Outlet)
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == invitationId
                && candidate.ConsumedAt == null
                && candidate.ExpiresAt >= now,
                cancellationToken);

        if (invitation is null)
        {
            return null;
        }

        invitation.User.PasswordHash = passwordHash;
        invitation.User.MustResetPassword = false;
        invitation.User.IsActive = true;
        invitation.User.UpdatedAt = now;
        invitation.ConsumedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(invitation.User);
    }

    public async Task<AuthenticationUser?> FindUserForPasswordChangeAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .Include(user => user.Outlet)
            .Where(user => user.Id == userId);
        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        var user = await query.FirstOrDefaultAsync(cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task UpdatePasswordAsync(
        long userId,
        string passwordHash,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {userId} not found");

        user.PasswordHash = passwordHash;
        user.UpdatedAt = updatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistRefreshTokenAsync(
        long userId,
        string refreshToken,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        dbContext.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuthenticationUser Map(User user) =>
        new(
            user.Id,
            user.BusinessId,
            user.Name,
            user.Email,
            user.PasswordHash,
            user.RoleId,
            user.Role?.Name,
            user.Role?.Permissions,
            user.OutletId,
            user.Outlet?.Name,
            user.PermissionOverrides,
            user.IsActive,
            user.MustResetPassword);

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
