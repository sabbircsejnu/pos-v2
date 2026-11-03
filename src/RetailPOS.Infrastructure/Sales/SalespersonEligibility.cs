using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

/// <summary>
/// The single definition of an eligible salesperson (SRS: "only active assigned outlet users"): an active user of the
/// Business whose primary outlet is the outlet or who holds an active assignment to it. Checkout validation and the POS
/// selector both use it, so the list a cashier sees and the rule the server enforces cannot drift apart.
/// </summary>
internal static class SalespersonEligibility
{
    public static IQueryable<User> Users(RetailPOSDbContext dbContext, long businessId, long outletId) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive
                && user.BusinessId == businessId
                && (user.OutletId == outletId
                    || dbContext.UserOutletAssignments.Any(assignment =>
                        assignment.UserId == user.Id
                        && assignment.OutletId == outletId
                        && assignment.BusinessId == businessId
                        && assignment.IsActive)));
}

public sealed class SaleSalespersonDirectory(RetailPOSDbContext dbContext) : ISaleSalespersonDirectory
{
    public Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken) =>
        dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == outletId)
            .Select(outlet => (long?)outlet.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EligibleSalespersonReadModel>> ListEligibleAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken) =>
        await SalespersonEligibility.Users(dbContext, businessId, outletId)
            // Users whose primary outlet is this outlet come first, then the other assigned users.
            .OrderByDescending(user => user.OutletId == outletId)
            .ThenBy(user => user.Name)
            .Select(user => new EligibleSalespersonReadModel(user.Id, user.Name, user.OutletId == outletId))
            .ToListAsync(cancellationToken);

    public Task<long?> FindOpenSessionOpenerAsync(long outletId, long terminalId, CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.OutletId == outletId
                && session.TerminalId == terminalId
                && session.Status == CashSessionStatuses.Open)
            .OrderByDescending(session => session.OpenedAt)
            .Select(session => (long?)session.OpenedByUserId)
            .FirstOrDefaultAsync(cancellationToken);
}
