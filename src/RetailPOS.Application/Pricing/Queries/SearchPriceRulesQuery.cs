using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Queries;

public sealed record SearchPriceRulesQuery(
    string? SearchQuery,
    string? RuleType,
    bool? IsActive,
    int PageNumber,
    int PageSize) : IQuery<PriceRuleSearchReadModel>;

public sealed class SearchPriceRulesQueryHandler(IPriceRuleReadStore store)
    : IRequestHandler<SearchPriceRulesQuery, PriceRuleSearchReadModel>
{
    public async Task<PriceRuleSearchReadModel> Handle(
        SearchPriceRulesQuery request,
        CancellationToken cancellationToken)
    {
        var result = await store.SearchAsync(
            request.SearchQuery,
            request.RuleType,
            request.IsActive,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        return new PriceRuleSearchReadModel(
            result.Rules,
            result.TotalCount,
            request.PageNumber,
            request.PageSize);
    }
}
