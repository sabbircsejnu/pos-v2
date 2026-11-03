using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Queries;

public sealed record GetCurrentUserQuery(long UserId) : IQuery<AuthenticationUserProfile?>;

public sealed class GetCurrentUserQueryValidator : AbstractValidator<GetCurrentUserQuery>
{
    public GetCurrentUserQueryValidator()
    {
        RuleFor(query => query.UserId).GreaterThan(0);
    }
}

public sealed class GetCurrentUserQueryHandler(IAuthenticationProfileStore profileStore)
    : IRequestHandler<GetCurrentUserQuery, AuthenticationUserProfile?>
{
    public async Task<AuthenticationUserProfile?> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var profile = await profileStore.FindProfileAsync(request.UserId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var permissions = LoginCommandHandler.BuildEffectivePermissions(
            profile.RolePermissions,
            profile.PermissionOverrides);
        return new AuthenticationUserProfile(
            profile.Id,
            profile.BusinessId,
            profile.Name,
            profile.Email,
            profile.RoleName,
            permissions,
            profile.OutletId,
            profile.OutletName,
            profile.RoleName,
            string.Equals(
                profile.RoleName,
                SystemRoleNames.BusinessOwner,
                StringComparison.OrdinalIgnoreCase),
            profile.MustResetPassword);
    }
}
