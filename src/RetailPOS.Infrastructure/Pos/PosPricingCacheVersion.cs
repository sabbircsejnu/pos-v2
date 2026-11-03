using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// Stores one opaque token per Business and per Business+Outlet in the distributed cache. Tokens never expire, so a
/// missing token means "never initialised" (or evicted) and is replaced by a fresh one instead of being reused.
/// </summary>
public sealed class PosPricingCacheVersion(
    IDistributedCache cache,
    ILogger<PosPricingCacheVersion> logger) : IPosPricingCacheVersion
{
    private const int InvalidationAttempts = 3;
    private readonly Dictionary<string, string> _tokensSeenByThisScope = [];

    public async Task<string?> GetTokenAsync(long businessId, long? outletId, CancellationToken cancellationToken)
    {
        try
        {
            var businessToken = await GetOrCreateAsync(BusinessKey(businessId), cancellationToken);
            if (!outletId.HasValue)
            {
                return businessToken;
            }

            var outletToken = await GetOrCreateAsync(OutletKey(businessId, outletId.Value), cancellationToken);
            return $"{businessToken}.{outletToken}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS cache version read failed for business {BusinessId}; bypassing the cache", businessId);
            return null;
        }
    }

    public async Task InvalidateAsync(long businessId, long? outletId, CancellationToken cancellationToken)
    {
        var key = outletId.HasValue ? OutletKey(businessId, outletId.Value) : BusinessKey(businessId);
        _tokensSeenByThisScope.Remove(key);
        for (var attempt = 1; attempt <= InvalidationAttempts; attempt++)
        {
            try
            {
                await cache.SetStringAsync(key, NewToken(), cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "POS cache invalidation attempt {Attempt}/{Attempts} failed for business {BusinessId} outlet {OutletId}",
                    attempt,
                    InvalidationAttempts,
                    businessId,
                    outletId);
            }
        }

        // Entries stay readable until their TTL (5 min prices, 10 min lookups); checkout never trusts the cache.
        logger.LogError(
            "POS cache invalidation could not be recorded for business {BusinessId} outlet {OutletId}; stale entries expire by TTL",
            businessId,
            outletId);
    }

    private async Task<string> GetOrCreateAsync(string key, CancellationToken cancellationToken)
    {
        if (_tokensSeenByThisScope.TryGetValue(key, out var known))
        {
            return known;
        }

        var token = await cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            token = NewToken();
            await cache.SetStringAsync(key, token, cancellationToken);
        }

        _tokensSeenByThisScope[key] = token;
        return token;
    }

    private static string NewToken() => Guid.NewGuid().ToString("N")[..12];

    private static string BusinessKey(long businessId) => $"pos:ver:{businessId}";

    private static string OutletKey(long businessId, long outletId) => $"pos:ver:{businessId}:{outletId}";
}
