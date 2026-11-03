using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;

namespace RetailPOS.Application.Sales.Queries;

public static class SalespersonDefaultSources
{
    public const string SessionOpener = "session_opener";
    public const string CurrentUser = "current_user";
    public const string None = "none";
}

public sealed record SalespersonOptionsReadModel(
    IReadOnlyList<EligibleSalespersonReadModel> Salespersons,
    long? DefaultSalespersonUserId,
    string DefaultSource);

/// <summary>
/// The salesperson choices for a POS terminal. Default (SRS: "Session opener … default salesperson if currently
/// eligible, but not exclusive seller"): the opener of the terminal's open session when eligible, otherwise the signed-in
/// user when eligible, otherwise none. The outlet must already be authorised by the caller.
/// </summary>
public sealed record GetEligibleSalespersonsQuery(long OutletId, long? TerminalId, long CurrentUserId)
    : IQuery<SalespersonOptionsReadModel>;

public sealed class GetEligibleSalespersonsQueryHandler(ISaleSalespersonDirectory directory)
    : IRequestHandler<GetEligibleSalespersonsQuery, SalespersonOptionsReadModel>
{
    public async Task<SalespersonOptionsReadModel> Handle(GetEligibleSalespersonsQuery request, CancellationToken cancellationToken)
    {
        var businessId = await directory.GetOutletBusinessIdAsync(request.OutletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {request.OutletId} not found");
        var eligible = await directory.ListEligibleAsync(businessId, request.OutletId, cancellationToken);
        var eligibleIds = eligible.Select(person => person.UserId).ToHashSet();

        if (request.TerminalId.HasValue)
        {
            var opener = await directory.FindOpenSessionOpenerAsync(request.OutletId, request.TerminalId.Value, cancellationToken);
            if (opener.HasValue && eligibleIds.Contains(opener.Value))
                return new SalespersonOptionsReadModel(eligible, opener, SalespersonDefaultSources.SessionOpener);
        }

        return eligibleIds.Contains(request.CurrentUserId)
            ? new SalespersonOptionsReadModel(eligible, request.CurrentUserId, SalespersonDefaultSources.CurrentUser)
            : new SalespersonOptionsReadModel(eligible, null, SalespersonDefaultSources.None);
    }
}
