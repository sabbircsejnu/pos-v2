using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Queries;

public sealed record SearchUsersQuery(
    int PageNumber,
    int PageSize,
    string? Search,
    long? RoleId,
    long? OutletId,
    bool? IsActive,
    long? BusinessId) : IQuery<UserListReadModel>;

public sealed class SearchUsersQueryHandler(IUserReadStore store)
    : IRequestHandler<SearchUsersQuery, UserListReadModel>
{
    public Task<UserListReadModel> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken) =>
        store.SearchAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.RoleId,
            request.OutletId,
            request.IsActive,
            request.BusinessId,
            cancellationToken);
}
