using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Infrastructure.Pos;

public sealed class PosProductLookupCache(
    IDistributedCache cache,
    ILogger<PosProductLookupCache> logger) : IPosProductLookupCache
{
    private static readonly TimeSpan LookupTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StockTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<PosProductLookupReadModel?> GetLookupAsync(
        PosProductLookupMode mode,
        string query,
        long? outletId,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(LookupKey(mode, query, outletId), cancellationToken);
            return json is null
                ? null
                : JsonSerializer.Deserialize<PosProductLookupReadModel>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS lookup cache read failed");
            return null;
        }
    }

    public async Task SetLookupAsync(
        PosProductLookupMode mode,
        string query,
        long? outletId,
        PosProductLookupReadModel result,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await cache.SetStringAsync(
                LookupKey(mode, query, outletId),
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LookupTtl },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS lookup cache write failed");
        }
    }

    public async Task<int?> GetStockAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await cache.GetStringAsync(StockKey(variantId, outletId), cancellationToken);
            return value is null ? null : int.Parse(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS stock cache read failed for variant {VariantId}", variantId);
            return null;
        }
    }

    public async Task SetStockAsync(
        long variantId,
        long outletId,
        int quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                StockKey(variantId, outletId),
                quantity.ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StockTtl },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS stock cache write failed for variant {VariantId}", variantId);
        }
    }

    private static string LookupKey(PosProductLookupMode mode, string query, long? outletId)
    {
        var outletKey = outletId ?? 0;
        return mode switch
        {
            PosProductLookupMode.Sku => $"pos:lookup:sku:{query}:{outletKey}",
            PosProductLookupMode.Variant when long.TryParse(query, out var variantId) =>
                $"pos:lookup:variant:{variantId}:{outletKey}",
            PosProductLookupMode.Variant => $"pos:lookup:variant:{query}:{outletKey}",
            _ => $"pos:lookup:barcode:{query}:{outletKey}"
        };
    }

    private static string StockKey(long variantId, long outletId) =>
        $"pos:stock:{variantId}:{outletId}";
}
