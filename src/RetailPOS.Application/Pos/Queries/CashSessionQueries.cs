using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Queries;

public sealed record GetActiveCashSessionQuery(long OutletId, long TerminalId)
    : IQuery<CashSessionReadModel?>;

public sealed class GetActiveCashSessionQueryHandler(
    ICashSessionStore store,
    ICashSessionOutletAuthorizer outletAuthorizer,
    TimeProvider clock)
    : IRequestHandler<GetActiveCashSessionQuery, CashSessionReadModel?>
{
    public async Task<CashSessionReadModel?> Handle(
        GetActiveCashSessionQuery request,
        CancellationToken cancellationToken)
    {
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var sessionId = await store.FindOpenSessionIdAsync(outletId, request.TerminalId, cancellationToken);
        if (sessionId is null)
            return null;

        var session = await store.FindAsync(sessionId.Value, cancellationToken);
        return session is null ? null : CashSessionMapper.ToReadModel(session, clock.GetUtcNow().UtcDateTime);
    }
}

public sealed record GetCashSessionCloseSummaryQuery(long SessionId)
    : IQuery<CashSessionCloseSummaryReadModel>;

public sealed record GetCashSessionOutletIdQuery(long SessionId) : IQuery<long>;

public sealed class GetCashSessionOutletIdQueryHandler(ICashSessionStore store)
    : IRequestHandler<GetCashSessionOutletIdQuery, long>
{
    public async Task<long> Handle(
        GetCashSessionOutletIdQuery request,
        CancellationToken cancellationToken)
    {
        return await store.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
    }
}

/// <summary>The live expected-cash computation (not a snapshot); the close handler recomputes it under the row lock.</summary>
public sealed class GetCashSessionCloseSummaryQueryHandler(
    ICashSessionStore store,
    ICashSessionOutletAuthorizer outletAuthorizer,
    TimeProvider clock)
    : IRequestHandler<GetCashSessionCloseSummaryQuery, CashSessionCloseSummaryReadModel>
{
    public async Task<CashSessionCloseSummaryReadModel> Handle(
        GetCashSessionCloseSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var outletId = await store.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeReadAsync(outletId, cancellationToken);

        var facts = await store.GetFactsAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        var closingAt = facts.Session.ClosedAt ?? clock.GetUtcNow().UtcDateTime;
        return CashSessionExpectedCashCalculator.Calculate(facts, closingAt);
    }
}

public sealed record ListCashSessionsQuery(
    long OutletId,
    long? TerminalId,
    DateTime? BusinessDateFrom,
    DateTime? BusinessDateTo,
    string? State,
    long? OpenedByUserId,
    int Page = 1,
    int PageSize = 20) : IQuery<CashSessionListPage>;

public sealed class ListCashSessionsQueryHandler(
    ICashSessionQueryStore queryStore,
    ICashSessionOutletAuthorizer outletAuthorizer)
    : IRequestHandler<ListCashSessionsQuery, CashSessionListPage>
{
    private static readonly string[] States = ["open", "overdue", "closed"];

    public async Task<CashSessionListPage> Handle(ListCashSessionsQuery request, CancellationToken cancellationToken)
    {
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim().ToLowerInvariant();
        if (state is not null && !States.Contains(state))
            throw new ArgumentException("State must be open, overdue or closed.");

        return await queryStore.ListAsync(
            new CashSessionListFilter(
                outletId,
                request.TerminalId,
                request.BusinessDateFrom?.Date,
                request.BusinessDateTo?.Date,
                state,
                request.OpenedByUserId,
                Math.Max(1, request.Page),
                Math.Clamp(request.PageSize, 1, 100)),
            cancellationToken);
    }
}

public sealed record GetCashSessionDetailQuery(long SessionId) : IQuery<CashSessionDetailReadModel>;

public sealed record GetCashSessionOpenContextQuery(long OutletId) : IQuery<CashSessionOpenContextReadModel>;

public sealed class GetCashSessionOpenContextQueryHandler(
    ICashSessionStore store,
    ICashSessionOutletAuthorizer outletAuthorizer,
    RetailPOS.Application.Settings.Abstractions.IBusinessPosSettingsStore settingsStore,
    TimeProvider clock)
    : IRequestHandler<GetCashSessionOpenContextQuery, CashSessionOpenContextReadModel>
{
    public async Task<CashSessionOpenContextReadModel> Handle(
        GetCashSessionOpenContextQuery request,
        CancellationToken cancellationToken)
    {
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var context = await store.GetOpenContextAsync(outletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {outletId} not found.");
        var settings = await settingsStore.GetAsync(context.BusinessId, cancellationToken);
        var zone = CashSessionTime.ResolveEffectiveTimeZoneId(context.OutletTimeZoneId, settings.DefaultTimeZoneId);
        var now = clock.GetUtcNow().UtcDateTime;
        var occupied = context.TerminalIdsWithOpenSession ?? [];
        return new CashSessionOpenContextReadModel(
            outletId,
            context.OutletName,
            context.ActiveTerminals
                .Select(terminal => new CashSessionTerminalOption(terminal.Id, terminal.Name, terminal.Code, occupied.Contains(terminal.Id)))
                .ToList(),
            context.ActiveTerminals.Count == 1 ? context.ActiveTerminals[0].Id : null,
            zone,
            CashSessionTime.BusinessDateFor(now, zone),
            now,
            settings.MaxCashSessionDurationHours);
    }
}

/// <summary>
/// Daily Sales for an outlet business date (default: the current one in the effective time zone). Read-only and
/// Business/Outlet scoped through the same authorizer as the session queries.
/// </summary>
public sealed record GetBusinessDaySalesQuery(long OutletId, DateTime? BusinessDate = null) : IQuery<BusinessDaySalesReadModel>;

public sealed class GetBusinessDaySalesQueryHandler(
    ICashSessionStore store,
    ICashSessionQueryStore queryStore,
    ICashSessionOutletAuthorizer outletAuthorizer,
    RetailPOS.Application.Settings.Abstractions.IBusinessPosSettingsStore settingsStore,
    TimeProvider clock)
    : IRequestHandler<GetBusinessDaySalesQuery, BusinessDaySalesReadModel>
{
    public async Task<BusinessDaySalesReadModel> Handle(GetBusinessDaySalesQuery request, CancellationToken cancellationToken)
    {
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var context = await store.GetOpenContextAsync(outletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {outletId} not found.");
        var settings = await settingsStore.GetAsync(context.BusinessId, cancellationToken);
        var zone = CashSessionTime.ResolveEffectiveTimeZoneId(context.OutletTimeZoneId, settings.DefaultTimeZoneId);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = CashSessionTime.BusinessDateFor(now, zone);
        var date = request.BusinessDate?.Date ?? today;
        if (date > today)
            throw new ArgumentException("The business date cannot be in the future.");

        var totals = await queryStore.GetBusinessDaySalesAsync(outletId, date, cancellationToken);
        return new BusinessDaySalesReadModel(
            outletId,
            context.OutletName,
            date,
            zone,
            now,
            totals.CompletedCount,
            totals.CompletedTotal,
            totals.CompletedTax,
            totals.CompletedTotal - totals.CompletedTax,
            totals.CompletedDiscount,
            totals.VoidedCount,
            totals.RefundedCount,
            totals.RefundedOriginalTotal);
    }
}

public sealed record ListCashDifferencesQuery(
    long OutletId,
    string? Status,
    string? PostingStatus,
    int Page = 1,
    int PageSize = 20) : IQuery<CashDifferenceListPage>;

public sealed class ListCashDifferencesQueryHandler(
    ICashSessionQueryStore queryStore,
    ICashSessionStore store,
    ICashSessionOutletAuthorizer outletAuthorizer)
    : IRequestHandler<ListCashDifferencesQuery, CashDifferenceListPage>
{
    public async Task<CashDifferenceListPage> Handle(ListCashDifferencesQuery request, CancellationToken cancellationToken)
    {
        var outletId = await outletAuthorizer.AuthorizeReadAsync(request.OutletId, cancellationToken);
        var context = await store.GetOpenContextAsync(outletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {outletId} not found.");
        return await queryStore.ListVariancesAsync(
            context.BusinessId,
            [outletId],
            new CashDifferenceListFilter(
                string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(request.PostingStatus) ? null : request.PostingStatus.Trim().ToLowerInvariant(),
                Math.Max(1, request.Page),
                Math.Clamp(request.PageSize, 1, 100)),
            cancellationToken);
    }
}

public sealed class GetCashSessionDetailQueryHandler(
    ICashSessionQueryStore queryStore,
    ICashSessionStore store,
    ICashSessionOutletAuthorizer outletAuthorizer,
    TimeProvider clock)
    : IRequestHandler<GetCashSessionDetailQuery, CashSessionDetailReadModel>
{
    public async Task<CashSessionDetailReadModel> Handle(GetCashSessionDetailQuery request, CancellationToken cancellationToken)
    {
        var outletId = await store.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeReadAsync(outletId, cancellationToken);
        return await queryStore.GetDetailAsync(request.SessionId, clock.GetUtcNow().UtcDateTime, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
    }
}
