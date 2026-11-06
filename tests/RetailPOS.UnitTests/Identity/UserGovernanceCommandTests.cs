using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Tests.Identity;

public sealed class UserGovernanceCommandTests
{
    [Fact]
    public async Task CreateUser_RejectsProtectedRoleBasedOnPersistedActorRole()
    {
        var store = new FakeUserGovernanceStore
        {
            Actor = new UserGovernanceActor(10, 1, "AccountsAdmin", true),
            Role = new UserGovernanceRole(3, "Super Admin")
        };
        var handler = new CreateUserCommandHandler(store, new TestGovernanceContext(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CreateUserCommand(Profile(3), "test-password"), CancellationToken.None));

        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(1, store.EmailExistsCalls);
    }

    [Fact]
    public async Task UpdateUser_RejectsPromotionBasedOnPersistedActorRole()
    {
        var store = new FakeUserGovernanceStore
        {
            Actor = new UserGovernanceActor(10, 1, "AccountsAdmin", true),
            Target = new UserGovernanceTarget(11, "target@example.test", 1, "AccountsAdmin", true),
            Role = new UserGovernanceRole(3, "Super Admin")
        };
        var handler = new UpdateUserCommandHandler(store, new TestGovernanceContext(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new UpdateUserCommand(11, Profile(3)), CancellationToken.None));

        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task UpdateUser_ProtectsTheLastActiveSuperAdminInsideTheCommand()
    {
        var store = new FakeUserGovernanceStore
        {
            Actor = new UserGovernanceActor(10, null, "Super Admin", true),
            Target = new UserGovernanceTarget(11, "target@example.test", 1, "Super Admin", true),
            Role = new UserGovernanceRole(2, "AccountsAdmin"),
            ActiveSuperAdminCount = 1
        };
        var handler = new UpdateUserCommandHandler(store, new TestGovernanceContext(10));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateUserCommand(11, Profile(2)), CancellationToken.None));

        Assert.Equal("At least one active Super Admin must remain.", exception.Message);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task SetActiveUser_RejectsLastSuperAdminDeactivation()
    {
        var store = new FakeUserGovernanceStore
        {
            Actor = new UserGovernanceActor(10, null, "Super Admin", true),
            Target = new UserGovernanceTarget(11, "target@example.test", 1, "Super Admin", true),
            ActiveSuperAdminCount = 1
        };
        var handler = new SetUserActiveCommandHandler(store, new TestGovernanceContext(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SetUserActiveCommand(11, false), CancellationToken.None));

        Assert.Equal(0, store.SetActiveCalls);
    }

    [Fact]
    public async Task DeleteUser_RejectsLastSuperAdminDeletion()
    {
        var store = new FakeUserGovernanceStore
        {
            Actor = new UserGovernanceActor(10, null, "Super Admin", true),
            Target = new UserGovernanceTarget(11, "target@example.test", 1, "Super Admin", true),
            ActiveSuperAdminCount = 1
        };
        var handler = new DeleteUserCommandHandler(store, new TestGovernanceContext(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteUserCommand(11), CancellationToken.None));

        Assert.Equal(0, store.DeleteCalls);
    }

    private static UserGovernanceProfile Profile(long roleId) => new(
        "Test User",
        "test@example.test",
        roleId,
        null,
        [],
        [],
        null,
        null,
        "all_locations",
        1,
        [],
        true);

    private sealed class TestGovernanceContext(long realUserId) : IUserGovernanceContext
    {
        public long? RealUserId { get; } = realUserId;
    }

    private sealed class FakeUserGovernanceStore : IUserGovernanceStore
    {
        public UserGovernanceActor? Actor { get; init; }
        public UserGovernanceTarget? Target { get; init; }
        public UserGovernanceRole? Role { get; init; }
        public long ActiveSuperAdminCount { get; init; }
        public int EmailExistsCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int SetActiveCalls { get; private set; }

        public Task<TResult> ExecuteSerializableAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<UserGovernanceActor?> FindActorAsync(long userId, CancellationToken cancellationToken) =>
            Task.FromResult(Actor?.Id == userId ? Actor : null);

        public Task<UserGovernanceTarget?> FindTargetAsync(
            long userId,
            long? businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Target?.Id == userId ? Target : null);

        public Task<UserGovernanceRole?> FindRoleAsync(long roleId, CancellationToken cancellationToken) =>
            Task.FromResult(Role?.Id == roleId ? Role : null);

        public Task<bool> EmailExistsAsync(
            string email,
            long? exceptUserId,
            CancellationToken cancellationToken)
        {
            EmailExistsCalls++;
            return Task.FromResult(false);
        }

        public Task<long> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ActiveSuperAdminCount);

        public Task<UserContextAccessReadModel?> FindContextAccessAsync(long userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserContextAccessReadModel?>(null);

        public Task<IReadOnlyList<string>> FindPermissionOverridesAsync(long userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<UserGovernanceContextRole>> ListBusinessContextRolesAsync(
            long businessId,
            IReadOnlyCollection<string> defaultRoleNames,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserGovernanceContextRole>>([]);

        public Task<UserReadModel> CreateAsync(
            UserGovernanceProfile profile,
            string password,
            long? businessId,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromException<UserReadModel>(
                new NotSupportedException("The test must reject before creating a User."));
        }

        public Task<UserReadModel> UpdateAsync(
            long userId,
            UserGovernanceProfile profile,
            bool actorIsSuperAdmin,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromException<UserReadModel>(
                new NotSupportedException("The test must reject before updating a User."));
        }

        public Task<bool> DeleteAsync(
            long userId,
            long? businessId,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> SetActiveAsync(
            long userId,
            bool isActive,
            long? businessId,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            SetActiveCalls++;
            return Task.FromResult(true);
        }
    }
}
