using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Abstractions;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.Infrastructure.Outlets;

public sealed class OutletWriteStore(
    RetailPOSDbContext dbContext,
    IOperationalLocationSourceEventWriter sourceEvents,
    ILogger<OutletWriteStore> logger,
    ICompanyTimeZoneProvider? timeZones = null,
    IAuditContext? auditContext = null,
    IAuditService? auditService = null) : IOutletWriteStore, IBusinessSetupOutletWriteStore
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
        var targetChange = await ApplySalesTargetsAsync(outlet, posSetup, isNew: true, cancellationToken);
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
        await AuditSalesTargetsAsync(outlet, targetChange, cancellationToken);

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
        var targetChange = await ApplySalesTargetsAsync(outlet, posSetup, isNew: false, cancellationToken);
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
        catch (DbUpdateException exception) when (
            Sales.PostgresViolations.IsUniqueViolation(exception, OutletSalesTargetModelConfiguration.VersionNumberIndex)
            || Sales.PostgresViolations.IsUniqueViolation(exception, OutletSalesTargetModelConfiguration.ActiveOverrideIndex))
        {
            throw new OutletSetupException(
                SalesTargetErrorCodes.Conflict,
                "Someone else changed this outlet's sales targets. Reload the page and try again.");
        }

        logger.LogInformation("Outlet updated: ID={Id}, Name={Name}", outlet.Id, outlet.Name);
        await AuditSalesTargetsAsync(outlet, targetChange, cancellationToken);
        return await ProjectOutletAsync(outlet.Id, businessId, cancellationToken);
    }

    private async Task<OutletSalesTargetChange?> ApplySalesTargetsAsync(
        Outlet outlet,
        OutletPosSetupInput posSetup,
        bool isNew,
        CancellationToken cancellationToken)
    {
        if (posSetup.SalesTargets is null)
            return null;
        if (timeZones is null)
            throw new InvalidOperationException("Saving sales targets needs the Company time zone provider.");
        if (outlet.BusinessId is not long businessId)
            throw new InvalidOperationException("Sales targets can only be set on an outlet that belongs to a business.");

        var today = DateOnly.FromDateTime(await timeZones.GetTodayAsync(businessId, cancellationToken));
        return await OutletSalesTargetApplier.ApplyAsync(
            dbContext,
            outlet,
            posSetup.SalesTargets,
            today,
            auditContext?.ActingUserId ?? auditContext?.RealUserId,
            isNew,
            cancellationToken);
    }

    /// <summary>The configuration rows are the record of what applied when; this adds who changed it and from what.</summary>
    private async Task AuditSalesTargetsAsync(
        Outlet outlet,
        OutletSalesTargetChange? change,
        CancellationToken cancellationToken)
    {
        if (auditService is null || change is null || !change.HasChanges)
            return;

        var entities = new List<AuditEntityInput>();
        if (change.NewVersion is { } version)
        {
            entities.Add(new AuditEntityInput(
                nameof(OutletSalesTargetVersion),
                version.Id.ToString(),
                AuditOperationType.Insert,
                [
                    ("versionNo", change.PreviousVersion?.VersionNo, version.VersionNo),
                    ("isEnabled", change.PreviousVersion?.IsEnabled, version.IsEnabled),
                    ("effectiveFrom", change.PreviousVersion?.EffectiveFrom.ToString("yyyy-MM-dd"), version.EffectiveFrom.ToString("yyyy-MM-dd")),
                    ("weeklyOffDays", DescribeOffDays(change.PreviousVersion?.Days.Where(day => day.IsOffDay).Select(day => day.DayOfWeek)), DescribeOffDays(version.Days.Where(day => day.IsOffDay).Select(day => day.DayOfWeek))),
                    ("weekdayTargets", DescribeTargets(change.PreviousVersion?.Days.Select(day => (day.DayOfWeek, day.TargetAmount))), DescribeTargets(version.Days.Select(day => (day.DayOfWeek, day.TargetAmount))))
                ]));
        }

        foreach (var item in change.ChangedOverrides)
            entities.Add(new AuditEntityInput(
                nameof(OutletSalesTargetOverride),
                item.Id.ToString(),
                AuditOperationType.Update,
                [
                    ("period", null, $"{item.PeriodType} {item.PeriodFrom:yyyy-MM-dd}..{item.PeriodTo:yyyy-MM-dd}"),
                    ("calculatedAmount", null, item.CalculatedAmount),
                    ("overrideAmount", null, item.OverrideAmount),
                    ("reason", null, item.Reason)
                ]));

        foreach (var item in change.ClearedOverrides)
            entities.Add(new AuditEntityInput(
                nameof(OutletSalesTargetOverride),
                item.Id.ToString(),
                AuditOperationType.Update,
                [
                    ("period", null, $"{item.PeriodType} {item.PeriodFrom:yyyy-MM-dd}..{item.PeriodTo:yyyy-MM-dd}"),
                    ("resetToAuto", item.OverrideAmount, null)
                ]));

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Settings,
            Summary = $"Outlet sales targets changed: {outlet.Id}",
            PrimaryEntity = (nameof(Outlet), outlet.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = outlet.BusinessId,
                ["outletId"] = outlet.Id
            },
            Entities = entities
        }, cancellationToken);
    }

    private static string? DescribeOffDays(IEnumerable<DayOfWeek>? days) =>
        days is null ? null : string.Join(",", days.OrderBy(day => day));

    private static string? DescribeTargets(IEnumerable<(DayOfWeek Day, decimal? Amount)>? days) =>
        days is null
            ? null
            : string.Join(",", days.OrderBy(day => day.Day).Select(day => $"{day.Day}={day.Amount?.ToString("0.00") ?? "-"}"));

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
