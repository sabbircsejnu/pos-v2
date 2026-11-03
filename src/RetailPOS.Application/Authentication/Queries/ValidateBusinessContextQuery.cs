using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Queries;

public sealed record BusinessContextResult(long? BusinessId, string? BusinessName);

public sealed record ValidateBusinessContextQuery(long UserId, long? BusinessId)
    : IQuery<BusinessContextResult>;

public sealed class ValidateBusinessContextQueryHandler(
    IAuthenticationProfileStore profiles,
    IBusinessAdministrationReadStore businesses)
    : IRequestHandler<ValidateBusinessContextQuery, BusinessContextResult>
{
    public async Task<BusinessContextResult> Handle(
        ValidateBusinessContextQuery request, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindProfileAsync(request.UserId, cancellationToken);
        if (profile is not { IsActive: true }
            || !string.Equals(profile.RoleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only an active Super Admin may select Business context.");

        if (request.BusinessId is null)
            return new BusinessContextResult(null, null);
        if (request.BusinessId <= 0)
            throw new ArgumentException("A positive Business identifier is required.");

        var business = await businesses.FindBusinessAsync(request.BusinessId.Value, cancellationToken);
        if (business is not { IsActive: true })
            throw new UnauthorizedAccessException("The selected Business is unavailable.");

        return new BusinessContextResult(business.BusinessId, business.BusinessName);
    }
}
