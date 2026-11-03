using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// Collects which Business/Outlet price and lookup caches a unit of work makes stale and invalidates them only after the
/// database commit: immediately after SaveChanges when no transaction is open, otherwise when that transaction commits.
/// A rollback or failed save drops the pending work, so a change that never committed never evicts anything.
/// </summary>
public sealed class PosPricingInvalidationTracker(IPosPricingCacheVersion version)
{
    private readonly HashSet<(long BusinessId, long? OutletId)> _pending = [];

    public void Capture(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Product product:
                    Add(product.BusinessId, null);
                    break;
                case ProductVariant variant:
                    Add(variant.BusinessId, null);
                    break;
                case OutletPriceOverride priceOverride:
                    Add(priceOverride.BusinessId, priceOverride.OutletId);
                    if (entry.State == EntityState.Modified)
                    {
                        Add(
                            entry.OriginalValues.GetValue<long>(nameof(OutletPriceOverride.BusinessId)),
                            entry.OriginalValues.GetValue<long>(nameof(OutletPriceOverride.OutletId)));
                    }

                    break;
                case PriceRule rule:
                    AddRule(rule.BusinessId, rule.RuleType, rule.TargetId);
                    if (entry.State == EntityState.Modified)
                    {
                        AddRule(
                            entry.OriginalValues.GetValue<long>(nameof(PriceRule.BusinessId)),
                            entry.OriginalValues.GetValue<string>(nameof(PriceRule.RuleType)),
                            entry.OriginalValues.GetValue<long?>(nameof(PriceRule.TargetId)));
                    }

                    break;
            }
        }
    }

    public void Clear() => _pending.Clear();

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var work = _pending.ToArray();
        _pending.Clear();
        foreach (var (businessId, outletId) in work)
        {
            await version.InvalidateAsync(businessId, outletId, cancellationToken);
        }
    }

    // An outlet-targeted rule only changes that outlet's prices; every other rule can affect any outlet of the Business.
    private void AddRule(long businessId, string ruleType, long? targetId) =>
        Add(businessId, string.Equals(ruleType, "outlet", StringComparison.OrdinalIgnoreCase) ? targetId : null);

    private void Add(long businessId, long? outletId)
    {
        if (businessId > 0)
        {
            _pending.Add((businessId, outletId));
        }
    }
}

public sealed class PosPricingSaveChangesInterceptor(PosPricingInvalidationTracker tracker) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (!HasOpenTransaction(eventData.Context))
        {
            tracker.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (!HasOpenTransaction(eventData.Context))
        {
            await tracker.FlushAsync(CancellationToken.None);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (!HasOpenTransaction(eventData.Context))
        {
            tracker.Clear();
        }
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is not null)
        {
            tracker.Capture(context.ChangeTracker);
        }
    }

    // The in-memory provider used by some tests ignores transactions, so a "current" one never commits.
    internal static bool HasOpenTransaction(DbContext? context) =>
        context is not null
        && context.Database.CurrentTransaction is not null
        && context.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;
}

public sealed class PosPricingTransactionInterceptor(PosPricingInvalidationTracker tracker) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        tracker.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        tracker.FlushAsync(CancellationToken.None);

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        tracker.Clear();

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        tracker.Clear();
        return Task.CompletedTask;
    }
}

public static class PosPricingCacheInvalidationExtensions
{
    public static IServiceCollection AddPosPricingCacheInvalidation(this IServiceCollection services)
    {
        services.AddScoped<IPosPricingCacheVersion, PosPricingCacheVersion>();
        services.AddScoped<PosPricingInvalidationTracker>();
        services.AddScoped<PosPricingSaveChangesInterceptor>();
        services.AddScoped<PosPricingTransactionInterceptor>();
        return services;
    }

    public static DbContextOptionsBuilder AddPosPricingCacheInvalidation(
        this DbContextOptionsBuilder options,
        IServiceProvider serviceProvider) =>
        options.AddInterceptors(
            serviceProvider.GetRequiredService<PosPricingSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<PosPricingTransactionInterceptor>());
}
