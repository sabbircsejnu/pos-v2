using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Queries;

public sealed record GetPriceRuleQuery(long Id) : IQuery<PriceRuleReadModel>;

public sealed class GetPriceRuleQueryHandler(IPriceRuleReadStore store)
    : IRequestHandler<GetPriceRuleQuery, PriceRuleReadModel>
{
    public async Task<PriceRuleReadModel> Handle(
        GetPriceRuleQuery request,
        CancellationToken cancellationToken)
    {
        return await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price rule with ID {request.Id} not found");
    }
}
