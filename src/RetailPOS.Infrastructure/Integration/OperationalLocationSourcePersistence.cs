using System.Globalization;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Integration;

internal static class OperationalLocationSourcePersistence
{
    public static Task SaveCreatedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Outlet source,
        CancellationToken cancellationToken) =>
        SaveCreatedCoreAsync(db, events, source, "OutletCreated", cancellationToken);

    public static Task SaveCreatedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Warehouse source,
        CancellationToken cancellationToken) =>
        SaveCreatedCoreAsync(db, events, source, "WarehouseCreated", cancellationToken);

    public static Task SaveUpdatedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Outlet source,
        CancellationToken cancellationToken) =>
        SaveUpdatedCoreAsync(db, events, source, "OutletUpdated", cancellationToken);

    public static Task SaveUpdatedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Warehouse source,
        CancellationToken cancellationToken) =>
        SaveUpdatedCoreAsync(db, events, source, "WarehouseUpdated", cancellationToken);

    public static Task SaveDeletedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Outlet source,
        CancellationToken cancellationToken) =>
        SaveDeletedCoreAsync(db, events, source, "OutletDeleted", cancellationToken);

    public static Task SaveDeletedAsync(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        Warehouse source,
        CancellationToken cancellationToken) =>
        SaveDeletedCoreAsync(db, events, source, "WarehouseDeleted", cancellationToken);

    public static OperationalLocationSourceEvent Snapshot(Outlet source, bool deleted = false)
    {
        return new OperationalLocationSourceEvent(
            source.BusinessId ?? 0, "RetailPOS", "Outlet",
            source.Id.ToString(CultureInfo.InvariantCulture), source.SourceVersion, deleted,
            source.Name, source.Address, source.ContactNumber,
            source.ManagerId?.ToString(CultureInfo.InvariantCulture), AsUtc(source.UpdatedAt));
    }

    public static OperationalLocationSourceEvent Snapshot(Warehouse source, bool deleted = false)
    {
        return new OperationalLocationSourceEvent(
            source.BusinessId ?? 0, "RetailPOS", "Warehouse",
            source.Id.ToString(CultureInfo.InvariantCulture), source.SourceVersion, deleted,
            source.Name, source.Address, null,
            source.ManagerId?.ToString(CultureInfo.InvariantCulture), AsUtc(source.UpdatedAt));
    }

    private static async Task SaveCreatedCoreAsync<T>(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        T source,
        string eventName,
        CancellationToken cancellationToken) where T : class
    {
        await InTransactionAsync(db, async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            var snapshot = source switch
            {
                Outlet outlet => Snapshot(outlet),
                Warehouse warehouse => Snapshot(warehouse),
                _ => throw new InvalidOperationException("Unsupported operational location source.")
            };
            await events.AppendAsync(eventName, snapshot, cancellationToken);
            if (snapshot.BusinessId > 0)
                await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task SaveUpdatedCoreAsync<T>(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        T source,
        string eventName,
        CancellationToken cancellationToken) where T : class
    {
        await InTransactionAsync(db, async () =>
        {
            var snapshot = source switch
            {
                Outlet outlet => Snapshot(IncrementVersion(outlet)),
                Warehouse warehouse => Snapshot(IncrementVersion(warehouse)),
                _ => throw new InvalidOperationException("Unsupported operational location source.")
            };
            if (snapshot.BusinessId > 0)
                await events.AppendAsync(eventName, snapshot, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task SaveDeletedCoreAsync<T>(
        RetailPOSDbContext db,
        IOperationalLocationSourceEventWriter events,
        T source,
        string eventName,
        CancellationToken cancellationToken) where T : class
    {
        await InTransactionAsync(db, async () =>
        {
            var snapshot = source switch
            {
                Outlet outlet => Snapshot(outlet, deleted: true) with { SourceVersion = checked(outlet.SourceVersion + 1) },
                Warehouse warehouse => Snapshot(warehouse, deleted: true) with { SourceVersion = checked(warehouse.SourceVersion + 1) },
                _ => throw new InvalidOperationException("Unsupported operational location source.")
            };
            if (snapshot.BusinessId > 0)
                await events.AppendAsync(eventName, snapshot, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private static Outlet IncrementVersion(Outlet source)
    {
        source.SourceVersion = checked(source.SourceVersion + 1);
        return source;
    }

    private static Warehouse IncrementVersion(Warehouse source)
    {
        source.SourceVersion = checked(source.SourceVersion + 1);
        return source;
    }

    private static async Task InTransactionAsync(
        RetailPOSDbContext db,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await operation();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await operation();
        await transaction.CommitAsync(cancellationToken);
    }

    private static DateTime AsUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
