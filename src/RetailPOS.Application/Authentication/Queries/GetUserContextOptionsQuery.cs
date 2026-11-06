using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Context;

namespace RetailPOS.Application.Authentication.Queries;

public sealed record GetUserContextOptionsQuery(long RealUserId) : IQuery<UserContextOptions>;

public sealed class GetUserContextOptionsQueryHandler(
    IAuthenticationStore store,
    UserContextPolicy policy)
    : IRequestHandler<GetUserContextOptionsQuery, UserContextOptions>
{
    public async Task<UserContextOptions> Handle(
        GetUserContextOptionsQuery request,
        CancellationToken cancellationToken) =>
        await policy.GetOptionsAsync(
            await store.FindActiveUserForRoleSwitchAsync(request.RealUserId, cancellationToken),
            cancellationToken);
}
