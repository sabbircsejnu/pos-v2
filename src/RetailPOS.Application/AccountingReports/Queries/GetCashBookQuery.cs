using RetailPOS.Application.Settings.Abstractions;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetCashBookQuery(CashBookFilter Filter, IReadOnlyList<string> AccountSlots)
    : IQuery<CashBookReadModel>;

public sealed class GetCashBookQueryHandler(
    ICashBookReadStore store,
    ICashBookPostingAccountResolver accountResolver,
    IAccountingReportTenantScope tenantScope,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<GetCashBookQuery, CashBookReadModel>
{
    private static readonly string[] DefaultSlots = ["cash", "petty_cash", "bank"];

    public async Task<CashBookReadModel> Handle(
        GetCashBookQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var dateFrom = request.Filter.DateFrom?.Date ?? await timeZones.GetTodayAsync(businessId, cancellationToken);
        var dateTo = (request.Filter.DateTo?.Date ?? await timeZones.GetTodayAsync(businessId, cancellationToken))
            .AddDays(1)
            .AddTicks(-1);
        if (dateTo < dateFrom)
            throw new InvalidOperationException("DateTo must not be earlier than DateFrom.");

        var requestedSlots = request.AccountSlots.Count == 0
            ? DefaultSlots
            : request.AccountSlots;
        var slots = requestedSlots
            .Select(slot => slot.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (slots.Any(slot => slot is not "cash" and not "petty_cash" and not "bank"))
            throw new InvalidOperationException(
                "Cash Book accounts must be configured Cash, Petty Cash, or Bank accounts.");

        var outletId = string.Equals(
            request.Filter.LocationType,
            "outlet",
            StringComparison.OrdinalIgnoreCase)
            ? request.Filter.LocationId
            : null;
        // Resolvers share the request's scoped persistence session.
        var resolutions = new List<CashBookAccountResolution>();
        foreach (var slot in slots)
        {
            resolutions.Add(await accountResolver.ResolveAsync(slot, outletId, cancellationToken));
        }

        return await store.GetAsync(
            request.Filter with { DateFrom = dateFrom, DateTo = dateTo },
            businessId,
            resolutions,
            cancellationToken);
    }
}
