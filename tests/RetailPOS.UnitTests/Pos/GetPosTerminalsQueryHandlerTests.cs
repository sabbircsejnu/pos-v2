using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Tests.Pos;

public sealed class GetPosTerminalsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsOutletAndCancellation()
    {
        var store = new FakePosTerminalReadStore();
        var handler = new GetPosTerminalsQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var terminals = await handler.Handle(
            new GetPosTerminalsQuery(5001),
            cancellation.Token);

        Assert.Same(store.Terminals, terminals);
        Assert.Equal(5001, store.OutletId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private sealed class FakePosTerminalReadStore : IPosTerminalReadStore
    {
        public IReadOnlyList<PosTerminalReadModel> Terminals { get; } =
            [new(8001, "Counter", "COUNTER-1", true)];
        public long OutletId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<PosTerminalReadModel>> GetActiveForOutletAsync(
            long outletId,
            CancellationToken cancellationToken)
        {
            OutletId = outletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Terminals);
        }
    }
}
