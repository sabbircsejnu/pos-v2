using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Tests.Pricing;

public sealed class PriceRuleCommandHandlerTests
{
    [Fact]
    public async Task CreateHandlerValidatesAndNormalizesBeforePersistence()
    {
        var store = new FakeStore();
        var handler = new CreatePriceRuleCommandHandler(store);
        using var cancellation = new CancellationTokenSource();
        var command = new CreatePriceRuleCommand(new PriceRuleWriteData(
            " Spring offer ",
            " Seasonal ",
            "GLOBAL",
            null,
            "PERCENTAGE",
            10m,
            1,
            5,
            null,
            null,
            true));

        var result = await handler.Handle(command, cancellation.Token);

        Assert.Equal("Spring offer", result.Name);
        Assert.Equal("Seasonal", store.CreatedRule?.Description);
        Assert.Equal("global", store.CreatedRule?.RuleType);
        Assert.Equal("percentage", store.CreatedRule?.DiscountType);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task CreateHandlerRejectsDuplicateNameUsingUntrimmedInput()
    {
        var store = new FakeStore { NameExists = true };
        var handler = new CreatePriceRuleCommandHandler(store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new CreatePriceRuleCommand(new PriceRuleWriteData(
                    " Spring offer ",
                    null,
                    "global",
                    null,
                    "fixed",
                    5m,
                    1,
                    0,
                    null,
                    null,
                    true)),
                CancellationToken.None));

        Assert.Equal("A price rule named ' Spring offer ' already exists", exception.Message);
        Assert.Equal(" Spring offer ", store.CheckedName);
        Assert.Null(store.CreatedRule);
    }

    [Fact]
    public async Task UpdateHandlerChecksExistenceBeforeValidation()
    {
        var store = new FakeStore();
        var handler = new UpdatePriceRuleCommandHandler(store);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new UpdatePriceRuleCommand(
                    404,
                    new PriceRuleWriteData(
                        "Rule",
                        null,
                        "invalid",
                        null,
                        "percentage",
                        10m,
                        1,
                        0,
                        null,
                        null,
                        true)),
                CancellationToken.None));

        Assert.Equal("Price rule with ID 404 not found", exception.Message);
        Assert.Equal(404, store.RequestedId);
        Assert.Null(store.CheckedName);
    }

    [Fact]
    public async Task DeleteHandlerRejectsMissingRule()
    {
        var handler = new DeletePriceRuleCommandHandler(new FakeStore());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(new DeletePriceRuleCommand(404), CancellationToken.None));

        Assert.Equal("Price rule with ID 404 not found", exception.Message);
    }

    private sealed class FakeStore : IPriceRuleCommandStore
    {
        public bool NameExists { get; init; }
        public PriceRuleWriteData? CreatedRule { get; private set; }
        public string? CheckedName { get; private set; }
        public long RequestedId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<bool> NameExistsAsync(
            string name,
            long? excludeId,
            CancellationToken cancellationToken)
        {
            CheckedName = name;
            CancellationToken = cancellationToken;
            return Task.FromResult(NameExists);
        }

        public Task<bool> TargetExistsAsync(
            string ruleType,
            long targetId,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        public Task<PriceRuleReadModel?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken)
        {
            RequestedId = id;
            CancellationToken = cancellationToken;
            return Task.FromResult<PriceRuleReadModel?>(null);
        }

        public Task<PriceRuleReadModel> CreateAsync(
            PriceRuleWriteData rule,
            CancellationToken cancellationToken)
        {
            CreatedRule = rule;
            CancellationToken = cancellationToken;
            return Task.FromResult(ToReadModel(rule));
        }

        public Task<PriceRuleReadModel?> UpdateAsync(
            long id,
            PriceRuleWriteData rule,
            CancellationToken cancellationToken) =>
            Task.FromResult<PriceRuleReadModel?>(ToReadModel(rule, id));

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        private static PriceRuleReadModel ToReadModel(PriceRuleWriteData rule, long id = 1) =>
            new(
                id,
                rule.Name,
                rule.Description,
                rule.RuleType,
                rule.TargetId,
                rule.DiscountType,
                rule.DiscountValue,
                rule.MinQuantity,
                rule.Priority,
                rule.ValidFrom,
                rule.ValidTo,
                rule.IsActive,
                DateTime.UnixEpoch);
    }
}
