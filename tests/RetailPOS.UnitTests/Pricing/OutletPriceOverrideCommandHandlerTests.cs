using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Tests.Pricing;

public sealed class OutletPriceOverrideCommandHandlerTests
{
    [Fact]
    public async Task CreateHandlerValidatesInLegacyOrderAndNormalizesType()
    {
        var store = new FakeStore();
        var handler = new CreateOutletPriceOverrideCommandHandler(store, new AllowAllAuthorizer());
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new CreateOutletPriceOverrideCommand(
                new OutletPriceOverrideWriteData(5001, 9801, "FIXED", 25m, true)),
            cancellation.Token);

        Assert.Equal("fixed", result.OverrideType);
        Assert.Equal("outlet,variant,duplicate,create", store.Calls);
        Assert.Equal("fixed", store.CreatedData?.OverrideType);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task CreateHandlerRejectsInvalidTypeBeforeTargetLookups()
    {
        var store = new FakeStore();
        var handler = new CreateOutletPriceOverrideCommandHandler(store, new AllowAllAuthorizer());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new CreateOutletPriceOverrideCommand(
                    new OutletPriceOverrideWriteData(5001, 9801, "discount", 25m, true)),
                CancellationToken.None));

        Assert.Equal(
            "Invalid OverrideType 'discount'. Must be 'fixed' or 'margin'",
            exception.Message);
        Assert.Equal(string.Empty, store.Calls);
    }

    [Fact]
    public async Task CreateHandlerPreservesOutletVariantDuplicateErrorOrder()
    {
        var store = new FakeStore { OutletExists = false, ProductVariantExists = false };
        var handler = new CreateOutletPriceOverrideCommandHandler(store, new AllowAllAuthorizer());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new CreateOutletPriceOverrideCommand(
                    new OutletPriceOverrideWriteData(9999, 9999, "fixed", 25m, true)),
                CancellationToken.None));

        Assert.Equal("Outlet with ID 9999 not found", exception.Message);
        Assert.Equal("outlet", store.Calls);
    }

    [Fact]
    public async Task UpdateHandlerChecksMissingOverrideBeforeTypeValidation()
    {
        var store = new FakeStore { Existing = null };
        var handler = new UpdateOutletPriceOverrideCommandHandler(store, new AllowAllAuthorizer());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new UpdateOutletPriceOverrideCommand(404, "invalid", 25m, true),
                CancellationToken.None));

        Assert.Equal("Outlet price override with ID 404 not found", exception.Message);
        Assert.Equal("get", store.Calls);
    }

    [Fact]
    public async Task DeleteHandlerPreservesMissingOverrideError()
    {
        var store = new FakeStore { Existing = null };
        var handler = new DeleteOutletPriceOverrideCommandHandler(store, new AllowAllAuthorizer());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new DeleteOutletPriceOverrideCommand(404),
                CancellationToken.None));

        Assert.Equal("Outlet price override with ID 404 not found", exception.Message);
        Assert.Equal("get", store.Calls);
    }

    private sealed class FakeStore : IOutletPriceOverrideCommandStore
    {
        public bool OutletExists { get; init; } = true;
        public bool ProductVariantExists { get; init; } = true;
        public bool PairExists { get; init; }
        public OutletPriceOverrideReadModel? Existing { get; init; } = Model();
        public OutletPriceOverrideWriteData? CreatedData { get; private set; }
        public string Calls { get; private set; } = string.Empty;
        public CancellationToken CancellationToken { get; private set; }

        public Task<bool> OutletExistsAsync(long outletId, CancellationToken cancellationToken)
        {
            Record("outlet");
            CancellationToken = cancellationToken;
            return Task.FromResult(OutletExists);
        }

        public Task<bool> ProductVariantExistsAsync(
            long productVariantId,
            CancellationToken cancellationToken)
        {
            Record("variant");
            CancellationToken = cancellationToken;
            return Task.FromResult(ProductVariantExists);
        }

        public Task<bool> ExistsAsync(
            long outletId,
            long productVariantId,
            CancellationToken cancellationToken)
        {
            Record("duplicate");
            CancellationToken = cancellationToken;
            return Task.FromResult(PairExists);
        }

        public Task<OutletPriceOverrideReadModel?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken)
        {
            Record("get");
            CancellationToken = cancellationToken;
            return Task.FromResult(Existing);
        }

        public Task<OutletPriceOverrideReadModel?> CreateAsync(
            OutletPriceOverrideWriteData priceOverride,
            CancellationToken cancellationToken)
        {
            Record("create");
            CreatedData = priceOverride;
            CancellationToken = cancellationToken;
            return Task.FromResult<OutletPriceOverrideReadModel?>(Model(
                priceOverride.OverrideType,
                priceOverride.OverrideValue,
                priceOverride.IsActive));
        }

        public Task<OutletPriceOverrideReadModel?> UpdateAsync(
            long id,
            OutletPriceOverrideUpdateData priceOverride,
            CancellationToken cancellationToken)
        {
            Record("update");
            CancellationToken = cancellationToken;
            return Task.FromResult<OutletPriceOverrideReadModel?>(Model(
                priceOverride.OverrideType,
                priceOverride.OverrideValue,
                priceOverride.IsActive));
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
        {
            Record("delete");
            CancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        private void Record(string call) =>
            Calls = string.IsNullOrEmpty(Calls) ? call : $"{Calls},{call}";

        private static OutletPriceOverrideReadModel Model(
            string overrideType = "fixed",
            decimal overrideValue = 25m,
            bool isActive = true) =>
            new(
                1,
                5001,
                "Outlet",
                9801,
                "Variant",
                "Product",
                overrideType,
                overrideValue,
                isActive,
                DateTime.UnixEpoch);
    }

    private sealed class AllowAllAuthorizer : IOutletPriceOverrideWriteAuthorizer
    {
        public Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken) =>
            Task.FromResult(outletId);
    }
}
