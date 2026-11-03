using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed class IdentityUserDirectory(RetailPOSDbContext db) : IIdentityUserDirectory
{
    public Task<IdentityUserDirectoryEntry?> FindAsync(
        long identityUserId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == identityUserId)
            .Select(user => new IdentityUserDirectoryEntry(user.Id, user.BusinessId, user.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}
