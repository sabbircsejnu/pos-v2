using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Outlets;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Outlets;

/// <summary>Applies the POS part of an Outlet create/edit (invoice prefix, time zone, terminals) before the single save.</summary>
internal static class OutletPosSetupApplier
{
    private const string OpenSessionStatus = "open";

    public static async Task ApplyAsync(
        RetailPOSDbContext db,
        Outlet outlet,
        OutletPosSetupInput setup,
        bool isNew,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);

        if (setup.InvoicePrefix is not null)
            await ApplyInvoicePrefixAsync(db, outlet, setup.InvoicePrefix, isNew, cancellationToken);

        if (setup.TimeZoneId is not null)
        {
            var timeZone = OutletPosSetupRules.NormalizeTimeZone(setup.TimeZoneId);
            outlet.TimeZoneId = string.IsNullOrEmpty(timeZone) ? null : timeZone;
        }

        if (setup.Terminals is not null)
        {
            if (!setup.CanManageTerminals)
                throw new UnauthorizedAccessException("Managing POS terminals requires the outlets.terminals.manage permission.");
            await ApplyTerminalsAsync(db, outlet, setup.Terminals, isNew, cancellationToken);
        }
    }

    private static async Task ApplyInvoicePrefixAsync(
        RetailPOSDbContext db,
        Outlet outlet,
        string requestedPrefix,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var prefix = OutletPosSetupRules.NormalizeInvoicePrefix(requestedPrefix);
        if (string.Equals(prefix, outlet.InvoicePrefix, StringComparison.Ordinal))
            return;

        if (!outlet.BusinessId.HasValue)
            throw new InvalidOperationException("An invoice prefix can only be set on an outlet that belongs to a business.");

        if (!isNew && outlet.InvoicePrefix is not null && await IsPrefixLockedAsync(db, outlet.Id, cancellationToken))
            throw new OutletSetupException(
                OutletSetupErrorCodes.InvoicePrefixLocked,
                "The invoice prefix cannot be changed because invoices were already issued with it.");

        var businessId = outlet.BusinessId.Value;
        var otherPrefixes = await db.Outlets
            .AsNoTracking()
            .Where(other => other.BusinessId == businessId && other.Id != outlet.Id && other.InvoicePrefix != null)
            .Select(other => other.InvoicePrefix!)
            .ToListAsync(cancellationToken);
        if (otherPrefixes.Contains(prefix, StringComparer.Ordinal))
            throw new OutletSetupException(
                OutletSetupErrorCodes.DuplicateInvoicePrefix,
                $"The invoice prefix '{prefix}' is already used by another outlet.");
        var clashing = otherPrefixes.FirstOrDefault(other => OutletPosSetupRules.CanProduceCollision(prefix, other));
        if (clashing is not null)
            throw new OutletSetupException(
                OutletSetupErrorCodes.InvoicePrefixAmbiguous,
                $"The invoice prefix '{prefix}' is too similar to '{clashing}' and could produce duplicate invoice numbers.");

        outlet.InvoicePrefix = prefix;
    }

    public static async Task<bool> IsPrefixLockedAsync(
        RetailPOSDbContext db,
        long outletId,
        CancellationToken cancellationToken) =>
        await db.SaleInvoiceCounters.AnyAsync(counter => counter.OutletId == outletId, cancellationToken)
        || await db.Sales.AnyAsync(sale => sale.OutletId == outletId && sale.InvoicePrefix != null, cancellationToken);

    private static async Task ApplyTerminalsAsync(
        RetailPOSDbContext db,
        Outlet outlet,
        IReadOnlyList<OutletTerminalInput> inputs,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var existing = isNew
            ? new List<PosTerminal>()
            : await db.PosTerminals.Where(terminal => terminal.OutletId == outlet.Id).ToListAsync(cancellationToken);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var touchedIds = inputs.Where(input => input.Id.HasValue).Select(input => input.Id!.Value).ToHashSet();
        foreach (var untouched in existing.Where(terminal => !touchedIds.Contains(terminal.Id)))
            names.Add(untouched.Name);

        var codes = existing.Select(terminal => terminal.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        foreach (var input in inputs)
        {
            var name = OutletPosSetupRules.NormalizeTerminalName(input.Name);
            if (!names.Add(name))
                throw new OutletSetupException(
                    OutletSetupErrorCodes.DuplicateTerminalName,
                    $"The outlet already has a terminal named '{name}'.");

            if (!input.Id.HasValue)
            {
                var code = NextCode(codes);
                codes.Add(code);
                var terminal = new PosTerminal
                {
                    Name = name,
                    Code = code,
                    IsActive = input.IsActive,
                    IsDefault = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                outlet.PosTerminals.Add(terminal);
                continue;
            }

            var current = existing.FirstOrDefault(terminal => terminal.Id == input.Id.Value)
                ?? throw new OutletSetupException(
                    OutletSetupErrorCodes.InvalidTerminal,
                    $"Terminal {input.Id.Value} does not belong to this outlet.");
            if (current.IsActive && !input.IsActive &&
                await db.CashSessions.AnyAsync(
                    session => session.TerminalId == current.Id && session.Status == OpenSessionStatus,
                    cancellationToken))
                throw new OutletSetupException(
                    OutletSetupErrorCodes.TerminalHasOpenSession,
                    $"Terminal '{current.Name}' has an open or overdue cash session and cannot be deactivated.");

            current.Name = name;
            current.IsActive = input.IsActive;
            current.UpdatedAt = now;
        }
    }

    public static async Task DeleteTerminalAsync(
        RetailPOSDbContext db,
        long outletId,
        long terminalId,
        CancellationToken cancellationToken)
    {
        var terminal = await db.PosTerminals
            .FirstOrDefaultAsync(item => item.Id == terminalId && item.OutletId == outletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Terminal {terminalId} was not found for outlet {outletId}.");
        if (await db.CashSessions.AnyAsync(session => session.TerminalId == terminalId, cancellationToken)
            || await db.Sales.AnyAsync(sale => sale.TerminalId == terminalId, cancellationToken)
            || await db.ReceiptPrintHistories.AnyAsync(history => history.TerminalId == terminalId, cancellationToken))
            throw new OutletSetupException(
                OutletSetupErrorCodes.TerminalHasHistory,
                "This terminal has sales or cash-session history. Deactivate it instead of deleting it.");

        db.PosTerminals.Remove(terminal);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NextCode(HashSet<string> codes)
    {
        for (var number = codes.Count + 1; ; number++)
        {
            var code = $"T{number:D2}";
            if (!codes.Contains(code))
                return code;
        }
    }

    /// <summary>Adds the POS setup (invoice prefix, time zone, terminals) to already projected outlets.</summary>
    public static async Task<IReadOnlyList<OutletReadModel>> AttachAsync(
        RetailPOSDbContext db,
        IReadOnlyList<OutletReadModel> outlets,
        CancellationToken cancellationToken)
    {
        if (outlets.Count == 0)
            return outlets;

        var ids = outlets.Select(outlet => outlet.Id).ToList();
        var setups = await db.Outlets
            .AsNoTracking()
            .Where(outlet => ids.Contains(outlet.Id))
            .Select(outlet => new { outlet.Id, outlet.InvoicePrefix, outlet.TimeZoneId })
            .ToDictionaryAsync(outlet => outlet.Id, cancellationToken);
        var terminals = await db.PosTerminals
            .AsNoTracking()
            .Where(terminal => ids.Contains(terminal.OutletId))
            .OrderBy(terminal => terminal.Id)
            .Select(terminal => new
            {
                terminal.OutletId,
                Model = new OutletTerminalReadModel(
                    terminal.Id,
                    terminal.Name,
                    terminal.Code,
                    terminal.IsActive,
                    terminal.CashSessions.Any() || terminal.Sales.Any(),
                    terminal.CashSessions.Any(session => session.Status == OpenSessionStatus))
            })
            .ToListAsync(cancellationToken);
        var lockedByCounter = await db.SaleInvoiceCounters
            .AsNoTracking()
            .Where(counter => ids.Contains(counter.OutletId))
            .Select(counter => counter.OutletId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var lockedBySale = await db.Sales
            .AsNoTracking()
            .Where(sale => ids.Contains(sale.OutletId) && sale.InvoicePrefix != null)
            .Select(sale => sale.OutletId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var locked = lockedByCounter.Concat(lockedBySale).ToHashSet();
        var terminalsByOutlet = terminals
            .GroupBy(item => item.OutletId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<OutletTerminalReadModel>)group.Select(item => item.Model).ToList());

        return outlets
            .Select(outlet =>
            {
                setups.TryGetValue(outlet.Id, out var setup);
                terminalsByOutlet.TryGetValue(outlet.Id, out var outletTerminals);
                return outlet with
                {
                    InvoicePrefix = setup?.InvoicePrefix,
                    InvoicePrefixLocked = locked.Contains(outlet.Id),
                    TimeZoneId = setup?.TimeZoneId,
                    Terminals = outletTerminals ?? []
                };
            })
            .ToList();
    }
}
