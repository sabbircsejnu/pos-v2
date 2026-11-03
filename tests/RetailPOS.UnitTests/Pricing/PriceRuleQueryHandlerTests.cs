using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.Queries;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Tests.Pricing;

public sealed class PriceRuleQueryHandlerTests
{
    [Fact]
    public async Task SearchHandler_ForwardsFiltersAndPreservesPagingMetadata()
    {
        var rules = new[]
        {
            new PriceRuleReadModel(
                21, "Spring", null, "global", null, "percentage", 10m, 1, 5,
                null, null, true, DateTime.UnixEpoch)
        };
        var store = new FakeStore
        {
            SearchResult = new PriceRuleSearchDataReadModel(rules, 13)
        };
        var handler = new SearchPriceRulesQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new SearchPriceRulesQuery("Spring", "global", true, 2, 7),
            cancellation.Token);

        Assert.Same(rules, result.Rules);
        Assert.Equal(13, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(7, result.PageSize);
        Assert.Equal(("Spring", "global", true, 2, 7), store.SearchArguments);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task GetHandler_ReturnsRequestedRuleAndForwardsCancellation()
    {
        var expected = new PriceRuleReadModel(
            21, "Spring", null, "global", null, "percentage", 10m, 1, 5,
            null, null, true, DateTime.UnixEpoch);
        var store = new FakeStore { Rule = expected };
        var handler = new GetPriceRuleQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetPriceRuleQuery(21), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(21, store.RuleId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task GetHandler_PreservesMissingRuleError()
    {
        var handler = new GetPriceRuleQueryHandler(new FakeStore());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(new GetPriceRuleQuery(404), CancellationToken.None));

        Assert.Equal("Price rule with ID 404 not found", exception.Message);
    }

    private sealed class FakeStore : IPriceRuleReadStore
    {
        public PriceRuleSearchDataReadModel SearchResult { get; init; } =
            new(Array.Empty<PriceRuleReadModel>(), 0);
        public PriceRuleReadModel? Rule { get; init; }
        public (string? SearchQuery, string? RuleType, bool? IsActive, int PageNumber, int PageSize)
            SearchArguments { get; private set; }
        public long RuleId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PriceRuleSearchDataReadModel> SearchAsync(
            string? searchQuery,
            string? ruleType,
            bool? isActive,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
        {
            SearchArguments = (searchQuery, ruleType, isActive, pageNumber, pageSize);
            CancellationToken = cancellationToken;
            return Task.FromResult(SearchResult);
        }

        public Task<PriceRuleReadModel?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken)
        {
            RuleId = id;
            CancellationToken = cancellationToken;
            return Task.FromResult(Rule);
        }
    }
}
