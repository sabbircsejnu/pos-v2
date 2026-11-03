using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Queries;

public sealed record GetSaleTodaySummaryQuery(long? OutletId)
    : IQuery<SaleTodaySummaryReadModel>;

public sealed class GetSaleTodaySummaryQueryHandler(ISaleTodaySummaryReadStore store)
    : IRequestHandler<GetSaleTodaySummaryQuery, SaleTodaySummaryReadModel>
{
    public async Task<SaleTodaySummaryReadModel> Handle(
        GetSaleTodaySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var startUtc = DateTime.UtcNow.Date;
        var lines = await store.GetAsync(
            request.OutletId,
            startUtc,
            startUtc.AddDays(1),
            cancellationToken);
        var paymentBreakdown = lines
            .GroupBy(sale => sale.PaymentMethod)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(sale => sale.TotalAmount));

        return new SaleTodaySummaryReadModel(
            lines.Count,
            lines.Sum(sale => sale.TotalAmount),
            lines.Sum(sale => sale.Discount),
            lines.Sum(sale => sale.Tax),
            paymentBreakdown);
    }
}
