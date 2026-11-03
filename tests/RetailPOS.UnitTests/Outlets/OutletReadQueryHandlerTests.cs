using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Abstractions;
using RetailPOS.Application.Outlets.Commands;
using RetailPOS.Application.Outlets.Queries;

namespace RetailPOS.Tests.Outlets;

public sealed class OutletReadQueryHandlerTests
{
    [Fact]
    public async Task GetOutlets_UsesTenantScopeAndPreservesNameSearchDispatch()
    {
        var expected = new[] { CreateOutlet(4, "Central") };
        var store = new FakeOutletReadStore { Result = expected };
        var scope = new FakeOutletScopeProvider { BusinessId = 17 };
        var handler = new GetOutletsQueryHandler(store, scope);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetOutletsQuery("tral"), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(17, scope.BusinessId);
        Assert.Equal("tral", store.SearchTerm);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    public sealed class OutletWriteCommandHandlerTests
    {
        [Fact]
        public async Task CreateAndUpdateCommands_ForwardBusinessScopeAndValues()
        {
            var store = new FakeOutletWriteStore();
            var scope = new FakeOutletScopeProvider { BusinessId = 42 };
            var createHandler = new CreateOutletCommandHandler(store, scope);
            var updateHandler = new UpdateOutletCommandHandler(store, scope);
            using var cancellation = new CancellationTokenSource();

            await createHandler.Handle(
                new CreateOutletCommand("New", "Address", "555", 9),
                cancellation.Token);
            Assert.Equal(("New", "Address", "555", 9L, 42L),
                (store.Name, store.Address, store.ContactNumber, store.ManagerId, store.BusinessId));
            Assert.Equal(cancellation.Token, store.CancellationToken);

            await updateHandler.Handle(
                new UpdateOutletCommand(7, "Updated", "New address", null, null),
                cancellation.Token);
            Assert.Equal(7, store.OutletId);
            Assert.Equal(("Updated", "New address", null, null, 42L),
                (store.Name, store.Address, store.ContactNumber, store.ManagerId, store.BusinessId));
            Assert.Equal(cancellation.Token, store.CancellationToken);
        }

        [Fact]
        public async Task CreateAndUpdateCommands_ForwardPosSetupAndDeleteTerminalUsesTenantScope()
        {
            var store = new FakeOutletWriteStore();
            var scope = new FakeOutletScopeProvider { BusinessId = 42 };
            var setup = new OutletPosSetupInput("DHK01", "Asia/Dhaka", [new OutletTerminalInput(null, "Counter 1")], true);

            await new CreateOutletCommandHandler(store, scope).Handle(
                new CreateOutletCommand("New", "Address", null, null, setup), CancellationToken.None);
            Assert.Same(setup, store.PosSetup);

            await new UpdateOutletCommandHandler(store, scope).Handle(
                new UpdateOutletCommand(7, "Updated", "Address", null, null), CancellationToken.None);
            Assert.Same(OutletPosSetupInput.None, store.PosSetup);

            await new DeleteOutletTerminalCommandHandler(store, scope).Handle(
                new DeleteOutletTerminalCommand(7, 99), CancellationToken.None);
            Assert.Equal((7L, 99L, (long?)42L), store.DeletedTerminal);
        }

        [Fact]
        public async Task DeleteCommand_UsesResolvedTenantScope()
        {
            var store = new FakeOutletWriteStore { DeleteResult = true };
            var handler = new DeleteOutletCommandHandler(
                store,
                new FakeOutletScopeProvider { BusinessId = 61 });
            using var cancellation = new CancellationTokenSource();

            Assert.True(await handler.Handle(new DeleteOutletCommand(3), cancellation.Token));

            Assert.Equal(3, store.OutletId);
            Assert.Equal(61, store.BusinessId);
            Assert.Equal(cancellation.Token, store.CancellationToken);
        }

        private sealed class FakeOutletWriteStore : IOutletWriteStore
        {
            public string Name { get; private set; } = string.Empty;
            public string Address { get; private set; } = string.Empty;
            public string? ContactNumber { get; private set; }
            public long? ManagerId { get; private set; }
            public long? BusinessId { get; private set; }
            public long OutletId { get; private set; }
            public bool DeleteResult { get; init; }
            public OutletPosSetupInput? PosSetup { get; private set; }
            public (long OutletId, long TerminalId, long? BusinessId)? DeletedTerminal { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public Task<OutletReadModel> CreateAsync(
                string name,
                string address,
                string? contactNumber,
                long? managerId,
                long? businessId,
                OutletPosSetupInput posSetup,
                CancellationToken cancellationToken)
            {
                PosSetup = posSetup;
                SetValues(0, name, address, contactNumber, managerId, businessId, cancellationToken);
                return Task.FromResult(new OutletReadModel(
                    1, name, address, contactNumber, managerId, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch));
            }

            public Task<OutletReadModel> UpdateAsync(
                long outletId,
                string name,
                string address,
                string? contactNumber,
                long? managerId,
                long? businessId,
                OutletPosSetupInput posSetup,
                CancellationToken cancellationToken)
            {
                PosSetup = posSetup;
                SetValues(outletId, name, address, contactNumber, managerId, businessId, cancellationToken);
                return Task.FromResult(new OutletReadModel(
                    outletId, name, address, contactNumber, managerId, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch));
            }

            public Task DeleteTerminalAsync(
                long outletId,
                long terminalId,
                long? businessId,
                CancellationToken cancellationToken)
            {
                DeletedTerminal = (outletId, terminalId, businessId);
                CancellationToken = cancellationToken;
                return Task.CompletedTask;
            }

            public Task<bool> DeleteAsync(
                long outletId,
                long? businessId,
                CancellationToken cancellationToken)
            {
                OutletId = outletId;
                BusinessId = businessId;
                CancellationToken = cancellationToken;
                return Task.FromResult(DeleteResult);
            }

            private void SetValues(
                long outletId,
                string name,
                string address,
                string? contactNumber,
                long? managerId,
                long? businessId,
                CancellationToken cancellationToken)
            {
                OutletId = outletId;
                Name = name;
                Address = address;
                ContactNumber = contactNumber;
                ManagerId = managerId;
                BusinessId = businessId;
                CancellationToken = cancellationToken;
            }
        }
    }

    [Fact]
    public async Task GetOutlets_UsesListWhenSearchTermIsBlank()
    {
        var store = new FakeOutletReadStore();
        var handler = new GetOutletsQueryHandler(
            store,
            new FakeOutletScopeProvider { BusinessId = null });

        await handler.Handle(new GetOutletsQuery("  "), CancellationToken.None);

        Assert.True(store.ListCalled);
        Assert.Null(store.SearchTerm);
    }

    [Fact]
    public async Task BusinessOutletQuery_ForwardsExplicitBusinessScope()
    {
        var store = new FakeOutletReadStore();
        var handler = new GetBusinessOutletsQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        await handler.Handle(new GetBusinessOutletsQuery(29), cancellation.Token);

        Assert.Equal(29, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailAndStatsQueries_UseResolvedTenantScope()
    {
        var store = new FakeOutletReadStore { OutletResult = CreateOutlet(8, "East") };
        var scope = new FakeOutletScopeProvider { BusinessId = 31 };
        var detailHandler = new GetOutletByIdQueryHandler(store, scope);
        var statsHandler = new GetOutletStatsQueryHandler(store, scope);

        Assert.Equal(8, (await detailHandler.Handle(
            new GetOutletByIdQuery(8), CancellationToken.None))?.Id);
        Assert.Equal(31, store.BusinessId);

        Assert.Equal(8, (await statsHandler.Handle(
            new GetOutletStatsQuery(8), CancellationToken.None))?.Id);
        Assert.Equal(31, store.BusinessId);
    }

    private static OutletReadModel CreateOutlet(long id, string name) =>
        new(id, name, "Address", null, null, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private sealed class FakeOutletScopeProvider : IOutletScopeProvider
    {
        public long? BusinessId { get; init; }

        public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(BusinessId);
    }

    private sealed class FakeOutletReadStore : IOutletReadStore
    {
        public IReadOnlyList<OutletReadModel> Result { get; init; } = [];
        public OutletReadModel? OutletResult { get; init; }
        public string? SearchTerm { get; private set; }
        public long? BusinessId { get; private set; }
        public bool ListCalled { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<OutletReadModel>> GetAllAsync(
            long? businessId,
            CancellationToken cancellationToken)
        {
            ListCalled = true;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<OutletReadModel>> SearchAsync(
            string searchTerm,
            long? businessId,
            CancellationToken cancellationToken)
        {
            SearchTerm = searchTerm;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<OutletReadModel?> GetByIdAsync(
            long outletId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(OutletResult);
        }

        public Task<IReadOnlyList<OutletReadModel>> GetByBusinessIdAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<OutletStatsReadModel?> GetStatsAsync(
            long outletId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(
                OutletResult is null
                    ? null
                    : new OutletStatsReadModel(OutletResult.Id, OutletResult.Name, OutletResult.UserCount, 0, 0));
        }
    }
}
