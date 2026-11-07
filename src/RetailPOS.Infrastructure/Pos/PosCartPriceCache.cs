using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Infrastructure.Pos;

public sealed class PosCartPriceCache(
    IDistributedCache cache,
    ILogger<PosCartPriceCache> logger) : IPosCartPriceCache
{
    private static readonly TimeSpan PriceTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<PosCartPriceReadModel?> GetAsync(
        string scope,
        long variantId,
        long? outletId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(
                PriceKey(scope, variantId, outletId, quantity),
                cancellationToken);
            return json is null
                ? null
                : JsonSerializer.Deserialize<PosCartPriceReadModel>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS cart price cache read failed for variant {VariantId}", variantId);
            return null;
        }
    }

    public async Task SetAsync(
        string scope,
        long variantId,
        long? outletId,
        decimal quantity,
        PosCartPriceReadModel result,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                PriceKey(scope, variantId, outletId, quantity),
                JsonSerializer.Serialize(result, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = PriceTtl },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "POS cart price cache write failed for variant {VariantId}", variantId);
        }
    }

    private static string PriceKey(string scope, long variantId, long? outletId, decimal quantity) =>
        $"pos:price:{scope}:{variantId}:{outletId ?? 0}:{quantity}";
}
