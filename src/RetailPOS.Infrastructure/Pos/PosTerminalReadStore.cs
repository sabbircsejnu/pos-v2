using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class PosTerminalReadStore(RetailPOSDbContext dbContext) : IPosTerminalReadStore
{
    public async Task<IReadOnlyList<PosTerminalReadModel>> GetActiveForOutletAsync(
        long outletId,
        CancellationToken cancellationToken) =>
        await dbContext.PosTerminals
            .AsNoTracking()
            .Where(terminal => terminal.OutletId == outletId && terminal.IsActive)
            .OrderByDescending(terminal => terminal.IsDefault)
            .ThenBy(terminal => terminal.Name)
            .Select(terminal => new PosTerminalReadModel(
                terminal.Id,
                terminal.Name,
                terminal.Code,
                terminal.IsDefault))
            .ToListAsync(cancellationToken);
}
