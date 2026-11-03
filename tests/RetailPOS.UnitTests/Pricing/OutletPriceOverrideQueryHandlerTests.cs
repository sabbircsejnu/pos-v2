using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.Queries;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Tests.Pricing;

public sealed class OutletPriceOverrideQueryHandlerTests
{
    [Fact]
    public async Task ListHandlerReturnsStoredRowsAndCountWithCancellation()
    {
        var overrides = new[]
        {
            new OutletPriceOverrideReadModel(
                4, 8, string.Empty, 12, "Default", "Product", "fixed", 10m,
                true, DateTime.UnixEpoch)
        };
        var store = new FakeStore { OutletOverrides = overrides };
        var handler = new GetOutletPriceOverridesQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetOutletPriceOverridesQuery(8),
            cancellation.Token);

        Assert.Same(overrides, result.Overrides);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(8, store.OutletId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailHandlerReturnsStoredOverride()
    {
        var expected = new OutletPriceOverrideReadModel(
            4, 8, "Outlet", 12, "Default", "Product", "fixed", 10m,
            true, DateTime.UnixEpoch);
        var store = new FakeStore { Override = expected };
        var handler = new GetOutletPriceOverrideQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetOutletPriceOverrideQuery(4),
            cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(4, store.OverrideId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailHandlerPreservesMissingOverrideError()
    {
        var handler = new GetOutletPriceOverrideQueryHandler(new FakeStore());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new GetOutletPriceOverrideQuery(404),
                CancellationToken.None));

        Assert.Equal("Outlet price override with ID 404 not found", exception.Message);
    }

    private sealed class FakeStore : IOutletPriceOverrideReadStore
    {
        public IReadOnlyList<OutletPriceOverrideReadModel> OutletOverrides { get; init; } =
            Array.Empty<OutletPriceOverrideReadModel>();
        public OutletPriceOverrideReadModel? Override { get; init; }
        public long OutletId { get; private set; }
        public long OverrideId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<OutletPriceOverrideReadModel>> GetByOutletAsync(
            long outletId,
            CancellationToken cancellationToken)
        {
            OutletId = outletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(OutletOverrides);
        }

        public Task<OutletPriceOverrideReadModel?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken)
        {
            OverrideId = id;
            CancellationToken = cancellationToken;
            return Task.FromResult(Override);
        }
    }
}
