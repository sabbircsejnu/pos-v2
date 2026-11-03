using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosTerminalReadStore
{
    Task<IReadOnlyList<PosTerminalReadModel>> GetActiveForOutletAsync(
        long outletId,
        CancellationToken cancellationToken);
}
