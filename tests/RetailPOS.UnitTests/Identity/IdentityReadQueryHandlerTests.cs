using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Queries;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Tests.Identity;

public sealed class GetRolesQueryHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesVisibilityAndBusinessScopeToTheReadPort()
    {
        var expected = new[]
        {
            new RoleReadModel(1, "Cashier", ["sales.view"], 3, DateTime.UnixEpoch, DateTime.UnixEpoch)
        };
        var store = new FakeRoleReadStore { Result = expected };
        var handler = new GetRolesQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetRolesQuery(true, 17), cancellation.Token);

        Assert.Same(expected, result);
        Assert.True(store.IsSuperAdmin);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private sealed class FakeRoleReadStore : IRoleReadStore
    {
        public IReadOnlyList<RoleReadModel> Result { get; init; } = [];
        public bool IsSuperAdmin { get; private set; }
        public long? BusinessId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<RoleReadModel?> FindByIdAsync(
            long roleId,
            bool isSuperAdmin,
            CancellationToken cancellationToken) =>
            Task.FromResult<RoleReadModel?>(null);

        public Task<IReadOnlyList<RoleReadModel>> ListAsync(
            bool isSuperAdmin,
            long? businessId,
            CancellationToken cancellationToken)
        {
            IsSuperAdmin = isSuperAdmin;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}

public sealed class SearchUsersQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAllFiltersBusinessScopeAndCancellation()
    {
        var expected = new UserListReadModel([], 7, 2, 25);
        var store = new FakeUserReadStore { Result = expected };
        var handler = new SearchUsersQueryHandler(store);
        using var cancellation = new CancellationTokenSource();
        var query = new SearchUsersQuery(2, 25, "smith", 4, 0, false, 17);

        var result = await handler.Handle(query, cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(2, store.PageNumber);
        Assert.Equal(25, store.PageSize);
        Assert.Equal("smith", store.Search);
        Assert.Equal(4, store.RoleId);
        Assert.Equal(0, store.OutletId);
        Assert.False(store.IsActive);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private sealed class FakeUserReadStore : IUserReadStore
    {
        public UserListReadModel Result { get; init; } = new([], 0, 1, 10);
        public int PageNumber { get; private set; }
        public int PageSize { get; private set; }
        public string? Search { get; private set; }
        public long? RoleId { get; private set; }
        public long? OutletId { get; private set; }
        public bool? IsActive { get; private set; }
        public long? BusinessId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public IReadOnlyList<UserReadModel> UsersResult { get; init; } = [];
        public long? ListedOutletId { get; private set; }
        public long? ListedRoleId { get; private set; }

        public Task<UserReadModel?> FindByIdAsync(
            long userId,
            long? businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserReadModel?>(null);

        public Task<IReadOnlyList<UserReadModel>> ListByOutletAsync(
            long outletId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            ListedOutletId = outletId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(UsersResult);
        }

        public Task<IReadOnlyList<UserReadModel>> ListByRoleAsync(
            long roleId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            ListedRoleId = roleId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(UsersResult);
        }

        public Task<UserListReadModel> SearchAsync(
            int pageNumber,
            int pageSize,
            string? search,
            long? roleId,
            long? outletId,
            bool? isActive,
            long? businessId,
            CancellationToken cancellationToken)
        {
            PageNumber = pageNumber;
            PageSize = pageSize;
            Search = search;
            RoleId = roleId;
            OutletId = outletId;
            IsActive = isActive;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
