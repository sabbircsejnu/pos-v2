using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Queries;

public sealed record GetPosTerminalsQuery(long OutletId)
    : IQuery<IReadOnlyList<PosTerminalReadModel>>;

public sealed class GetPosTerminalsQueryHandler(IPosTerminalReadStore store)
    : IRequestHandler<GetPosTerminalsQuery, IReadOnlyList<PosTerminalReadModel>>
{
    public Task<IReadOnlyList<PosTerminalReadModel>> Handle(
        GetPosTerminalsQuery request,
        CancellationToken cancellationToken) =>
        store.GetActiveForOutletAsync(request.OutletId, cancellationToken);
}
