using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Queries;

public sealed record GetSaleTodaySummaryQuery(long? OutletId)
    : IQuery<SaleTodaySummaryReadModel>;

public sealed class GetSaleTodaySummaryQueryHandler(
    ISaleTodaySummaryReadStore store,
    ISaleReadScopeProvider scopeProvider,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<GetSaleTodaySummaryQuery, SaleTodaySummaryReadModel>
{
    public async Task<SaleTodaySummaryReadModel> Handle(
        GetSaleTodaySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(request.OutletId, cancellationToken);
        var zone = await companyTimeZone.GetTimeZoneIdAsync(cancellationToken);
        var today = CompanyDates.CalendarDateOf(DateTime.UtcNow, zone);
        var (startUtc, endExclusiveUtc) = CompanyDates.DayWindow(today, today, zone);
        var lines = await store.GetAsync(
            scope,
            startUtc!.Value,
            endExclusiveUtc!.Value,
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
