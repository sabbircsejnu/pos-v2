using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.Infrastructure.Outlets;

public sealed class OutletWriteStore(
    RetailPOSDbContext dbContext,
    IOperationalLocationSourceEventWriter sourceEvents,
    ILogger<OutletWriteStore> logger) : IOutletWriteStore, IBusinessSetupOutletWriteStore
{
    public async Task<OutletReadModel> CreateAsync(
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        long? businessId,
        OutletPosSetupInput posSetup,
        CancellationToken cancellationToken)
    {
        if (await ExistsByNameAsync(name, null, businessId, cancellationToken))
        {
            logger.LogWarning("Outlet creation failed: Name '{Name}' already exists", name);
            throw new InvalidOperationException($"An outlet with the name '{name}' already exists");
        }

        if (businessId.HasValue)
        {
            var maximumOutlets = await dbContext.Businesses
                .AsNoTracking()
                .Where(business => business.Id == businessId.Value)
                .Select(business => business.MaxOutlets)
                .FirstOrDefaultAsync(cancellationToken);

            if (maximumOutlets.HasValue)
            {
                var currentCount = await dbContext.Outlets
                    .LongCountAsync(outlet => outlet.BusinessId == businessId.Value, cancellationToken);
                if (currentCount >= maximumOutlets.Value)
                    throw new InvalidOperationException(
                        $"Outlet limit reached for this business ({maximumOutlets.Value}). Upgrade subscription or increase outlet limit.");
            }
        }

        await EnsureManagerExistsAsync(managerId, businessId, "creation", cancellationToken);

        var now = DateTime.UtcNow;
        var outlet = new Outlet
        {
            BusinessId = businessId,
            Name = name,
            Address = address,
            ContactNumber = contactNumber,
            ManagerId = managerId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await OutletPosSetupApplier.ApplyAsync(dbContext, outlet, posSetup, isNew: true, cancellationToken);
        dbContext.Outlets.Add(outlet);
        try
        {
            await OperationalLocationSourcePersistence.SaveCreatedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        }
        catch (DbUpdateException exception) when (Sales.PostgresViolations.IsUniqueViolation(exception, "invoice_prefix"))
        {
            dbContext.Entry(outlet).State = EntityState.Detached;
            throw new OutletSetupException(
                OutletSetupErrorCodes.DuplicateInvoicePrefix,
                "The invoice prefix is already used by another outlet.");
        }

        logger.LogInformation("Outlet created: ID={Id}, Name={Name}", outlet.Id, outlet.Name);

        var created = await ProjectOutletAsync(outlet.Id, businessId, cancellationToken);
        return created with { UserCount = 0 };
    }

    public async Task<OutletReadModel> UpdateAsync(
        long outletId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        long? businessId,
        OutletPosSetupInput posSetup,
        CancellationToken cancellationToken)
    {
        var outlet = await FindOutletAsync(outletId, businessId)
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet is null)
        {
            logger.LogWarning("Outlet update failed: Outlet with ID {Id} not found", outletId);
            throw new InvalidOperationException($"Outlet with ID {outletId} not found");
        }

        if (await ExistsByNameAsync(name, outletId, businessId, cancellationToken))
        {
            logger.LogWarning("Outlet update failed: Name '{Name}' already exists", name);
            throw new InvalidOperationException($"An outlet with the name '{name}' already exists");
        }

        await EnsureManagerExistsAsync(managerId, businessId, "update", cancellationToken);

        outlet.Name = name;
        outlet.Address = address;
        outlet.ContactNumber = contactNumber;
        outlet.ManagerId = managerId;
        outlet.UpdatedAt = DateTime.UtcNow;

        await OutletPosSetupApplier.ApplyAsync(dbContext, outlet, posSetup, isNew: false, cancellationToken);
        try
        {
            await OperationalLocationSourcePersistence.SaveUpdatedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        }
        catch (DbUpdateException exception) when (Sales.PostgresViolations.IsUniqueViolation(exception, "invoice_prefix"))
        {
            throw new OutletSetupException(
                OutletSetupErrorCodes.DuplicateInvoicePrefix,
                "The invoice prefix is already used by another outlet.");
        }

        logger.LogInformation("Outlet updated: ID={Id}, Name={Name}", outlet.Id, outlet.Name);
        return await ProjectOutletAsync(outlet.Id, businessId, cancellationToken);
    }

    public async Task DeleteTerminalAsync(
        long outletId,
        long terminalId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        if (!await FindOutletAsync(outletId, businessId).AnyAsync(cancellationToken))
            throw new KeyNotFoundException($"Outlet with ID {outletId} not found");

        await OutletPosSetupApplier.DeleteTerminalAsync(dbContext, outletId, terminalId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var outlet = await FindOutletAsync(outletId, businessId)
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet is null)
        {
            logger.LogWarning("Outlet deletion failed: Outlet with ID {Id} not found", outletId);
            return false;
        }

        var userCount = await GetUserCountAsync(outletId, businessId, cancellationToken);
        if (userCount > 0)
        {
            logger.LogWarning("Outlet deletion failed: Outlet {Id} has {Count} active users", outletId, userCount);
            throw new InvalidOperationException($"Cannot delete outlet. It has {userCount} active user(s)");
        }

        dbContext.Outlets.Remove(outlet);
        await OperationalLocationSourcePersistence.SaveDeletedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        logger.LogInformation("Outlet deleted: ID={Id}, Name={Name}", outletId, outlet.Name);
        return true;
    }

    public async Task<OutletReadModel> CreateForBusinessAsync(
        long businessId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        CancellationToken cancellationToken)
    {
        if (await ExistsByNameAsync(name, null, businessId, cancellationToken))
            throw new InvalidOperationException(
                $"An outlet with the name '{name}' already exists in this business");

        var business = await dbContext.Businesses
            .AsNoTracking()
            .Where(candidate => candidate.Id == businessId)
            .Select(candidate => new { candidate.MaxOutlets })
            .FirstOrDefaultAsync(cancellationToken);
        if (business is null)
            throw new InvalidOperationException($"Business {businessId} not found");

        if (business.MaxOutlets.HasValue)
        {
            var currentCount = await dbContext.Outlets
                .LongCountAsync(outlet => outlet.BusinessId == businessId, cancellationToken);
            if (currentCount >= business.MaxOutlets.Value)
                throw new InvalidOperationException(
                    $"Outlet limit reached ({business.MaxOutlets.Value}). Upgrade subscription or increase outlet limit.");
        }

        await EnsureBusinessManagerExistsAsync(managerId, businessId, cancellationToken);

        var now = DateTime.UtcNow;
        var outlet = new Outlet
        {
            BusinessId = businessId,
            Name = name,
            Address = address,
            ContactNumber = contactNumber,
            ManagerId = managerId,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Outlets.Add(outlet);
        await OperationalLocationSourcePersistence.SaveCreatedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        logger.LogInformation(
            "Business setup: Outlet created for business {BusinessId}: ID={Id}, Name={Name}",
            businessId,
            outlet.Id,
            outlet.Name);
        var created = await ProjectOutletAsync(outlet.Id, businessId, cancellationToken);
        return created with { UserCount = 0 };
    }

    public async Task<OutletReadModel> UpdateForBusinessAsync(
        long businessId,
        long outletId,
        string name,
        string address,
        string? contactNumber,
        long? managerId,
        CancellationToken cancellationToken)
    {
        var outlet = await FindOutletAsync(outletId, businessId)
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet is null)
            throw new InvalidOperationException($"Outlet {outletId} not found in business {businessId}");

        if (await ExistsByNameAsync(name, outletId, businessId, cancellationToken))
            throw new InvalidOperationException(
                $"An outlet with the name '{name}' already exists in this business");

        await EnsureBusinessManagerExistsAsync(managerId, businessId, cancellationToken);

        outlet.Name = name;
        outlet.Address = address;
        outlet.ContactNumber = contactNumber;
        outlet.ManagerId = managerId;
        outlet.UpdatedAt = DateTime.UtcNow;
        await OperationalLocationSourcePersistence.SaveUpdatedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        logger.LogInformation(
            "Business setup: Outlet updated for business {BusinessId}: ID={Id}",
            businessId,
            outlet.Id);
        return await ProjectOutletAsync(outlet.Id, businessId, cancellationToken);
    }

    public async Task<bool> DeleteForBusinessAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken)
    {
        var outlet = await FindOutletAsync(outletId, businessId)
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet is null)
            return false;

        var userCount = await GetUserCountAsync(outletId, businessId, cancellationToken);
        if (userCount > 0)
            throw new InvalidOperationException($"Cannot delete outlet. It has {userCount} active user(s)");

        dbContext.Outlets.Remove(outlet);
        await OperationalLocationSourcePersistence.SaveDeletedAsync(dbContext, sourceEvents, outlet, cancellationToken);
        logger.LogInformation(
            "Business setup: Outlet deleted from business {BusinessId}: ID={Id}",
            businessId,
            outletId);
        return true;
    }

    private IQueryable<Outlet> FindOutletAsync(long outletId, long? businessId)
    {
        var query = dbContext.Outlets.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(outlet => outlet.BusinessId == businessId.Value);
        return query.Where(outlet => outlet.Id == outletId);
    }

    private async Task<bool> ExistsByNameAsync(
        string name,
        long? excludeId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Outlets.Where(outlet => outlet.Name.ToLower() == name.ToLower());
        if (businessId.HasValue)
            query = query.Where(outlet => outlet.BusinessId == businessId.Value);
        if (excludeId.HasValue)
            query = query.Where(outlet => outlet.Id != excludeId.Value);
        return await query.AnyAsync(cancellationToken);
    }

    private async Task EnsureManagerExistsAsync(
        long? managerId,
        long? businessId,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!managerId.HasValue)
            return;

        var query = dbContext.Users.AsNoTracking().Where(user => user.Id == managerId.Value);
        if (businessId.HasValue)
            query = query.Where(user => user.BusinessId == businessId.Value);
        if (await query.AnyAsync(cancellationToken))
            return;

        logger.LogWarning(
            "Outlet {Operation} failed: Manager with ID {ManagerId} not found",
            operation,
            managerId);
        throw new InvalidOperationException($"Manager with ID {managerId} not found");
    }

    private async Task EnsureBusinessManagerExistsAsync(
        long? managerId,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!managerId.HasValue)
            return;

        var exists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == managerId.Value && user.BusinessId == businessId,
                cancellationToken);
        if (!exists)
            throw new InvalidOperationException(
                $"Manager with ID {managerId} not found in this business");
    }

    private async Task<int> GetUserCountAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users
            .Where(user => user.OutletId == outletId && user.IsActive);
        if (businessId.HasValue)
            query = query.Where(user => user.BusinessId == businessId.Value);
        return await query.CountAsync(cancellationToken);
    }

    private async Task<OutletReadModel> ProjectOutletAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var projected = (await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == outletId)
            .Where(outlet => !businessId.HasValue || outlet.BusinessId == businessId.Value)
            .Select(outlet => new OutletReadModel(
                outlet.Id,
                outlet.Name,
                outlet.Address,
                outlet.ContactNumber,
                outlet.ManagerId,
                outlet.Manager == null ? null : outlet.Manager.Name,
                dbContext.Users.Count(user =>
                    user.OutletId == outlet.Id
                    && user.IsActive
                    && (!businessId.HasValue || user.BusinessId == businessId.Value)),
                outlet.CreatedAt,
                outlet.UpdatedAt,
                null,
                false,
                null,
                null))
            .FirstOrDefaultAsync(cancellationToken))
            ?? throw new InvalidOperationException($"Outlet with ID {outletId} not found");
        return (await OutletPosSetupApplier.AttachAsync(dbContext, [projected], cancellationToken))[0];
    }
}
