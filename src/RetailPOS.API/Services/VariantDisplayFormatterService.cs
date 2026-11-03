using System.Globalization;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.Products.Queries;

namespace RetailPOS.API.Services;

public class VariantDisplayFormatterService : IVariantDisplayFormatterService
{
    private readonly ISender _sender;

    private List<VariationMeta>? _orderedVariations;
    private Dictionary<string, VariationMeta>? _variationLookup;

    public VariantDisplayFormatterService(ISender sender)
    {
        _sender = sender;
    }

    public async Task<string?> FormatAsync(
        string? rawAttributes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawAttributes))
            return null;

        await EnsureVariationCacheAsync(cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(rawAttributes);
            var parts = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => ExtractFromArray(doc.RootElement),
                JsonValueKind.Object => ExtractFromObject(doc.RootElement),
                _ => new List<VariantPart>()
            };

            if (!parts.Any())
                return null;

            return string.Join(" / ", parts.Select(p => $"{p.Label} {p.Value}"));
        }
        catch
        {
            // Raw strings like "Blue - 50 - Korean Georgette" cannot be safely labeled.
            return null;
        }
    }

    private async Task EnsureVariationCacheAsync(CancellationToken cancellationToken)
    {
        if (_orderedVariations != null && _variationLookup != null)
            return;

        var variations = await _sender.Send(new GetVariantDisplayMetadataQuery(), cancellationToken);

        _orderedVariations = variations
            .Select(v => new VariationMeta(
                v.Name,
                Normalize(v.Name),
                v.DisplayOrder,
                v.ActiveOptionNames.ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        _variationLookup = _orderedVariations
            .ToDictionary(v => v.NormalizedName, v => v, StringComparer.OrdinalIgnoreCase);
    }

    private List<VariantPart> ExtractFromObject(JsonElement obj)
    {
        var result = new List<VariantPart>();
        var fallbackOrder = (_orderedVariations?.Count ?? 0) + 1;

        foreach (var prop in obj.EnumerateObject())
        {
            var value = ReadScalarValue(prop.Value);
            if (!IsDisplayValue(value))
                continue;

            var normalized = Normalize(prop.Name);
            if (!TryResolveVariation(normalized, out var variation))
            {
                result.Add(new VariantPart(Humanize(prop.Name), value!, fallbackOrder++, result.Count));
                continue;
            }

            if (!IsActiveOptionValue(variation!, value!))
                continue;

            result.Add(new VariantPart(variation!.Name, value!, variation.DisplayOrder, result.Count));
        }

        return result
            .OrderBy(v => v.DisplayOrder)
            .ThenBy(v => v.Sequence)
            .ToList();
    }

    private List<VariantPart> ExtractFromArray(JsonElement arr)
    {
        var result = new List<VariantPart>();
        var fallbackOrder = (_orderedVariations?.Count ?? 0) + 1;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var label = ReadTextProperty(item, "displayName")
                ?? ReadTextProperty(item, "attributeName")
                ?? ReadTextProperty(item, "name")
                ?? ReadTextProperty(item, "key");

            var value = ReadTextProperty(item, "displayValue")
                ?? ReadTextProperty(item, "value");

            if (!IsDisplayValue(label) || !IsDisplayValue(value))
                continue;

            if (IsInactiveAttribute(item))
                continue;

            var normalized = Normalize(label!);
            if (!TryResolveVariation(normalized, out var variation))
            {
                result.Add(new VariantPart(Humanize(label!), value!, fallbackOrder++, result.Count));
                continue;
            }

            if (!IsActiveOptionValue(variation!, value!))
                continue;

            result.Add(new VariantPart(variation!.Name, value!, variation.DisplayOrder, result.Count));
        }

        return result
            .OrderBy(v => v.DisplayOrder)
            .ThenBy(v => v.Sequence)
            .ToList();
    }

    private bool TryResolveVariation(string normalizedLabel, out VariationMeta? variation)
    {
        variation = null;
        if (_variationLookup == null)
            return false;

        return _variationLookup.TryGetValue(normalizedLabel, out variation);
    }

    private static string? ReadTextProperty(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var property))
            return null;

        return ReadScalarValue(property);
    }

    private static string? ReadScalarValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => null
        };
    }

    private static bool IsInactiveAttribute(JsonElement item)
    {
        var status = ReadTextProperty(item, "status");
        if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            return true;

        if (item.TryGetProperty("isActive", out var isActive) && isActive.ValueKind is JsonValueKind.False)
            return true;

        if (item.TryGetProperty("active", out var active) && active.ValueKind is JsonValueKind.False)
            return true;

        if (item.TryGetProperty("isDeleted", out var isDeleted) && isDeleted.ValueKind is JsonValueKind.True)
            return true;

        if (item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind is JsonValueKind.True)
            return true;

        return false;
    }

    private static bool IsDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return !string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalized, "inactive", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalized, "deleted", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string Humanize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var withSpaces = key
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);

        var parts = withSpaces
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant()));

        return string.Join(" ", parts);
    }

    private static bool IsActiveOptionValue(VariationMeta variation, string value)
    {
        if (variation.ActiveOptions.Count == 0)
            return true;

        return variation.ActiveOptions.Contains(value.Trim());
    }

    private sealed record VariationMeta(string Name, string NormalizedName, int DisplayOrder, HashSet<string> ActiveOptions);
    private sealed record VariantPart(string Label, string Value, int DisplayOrder, int Sequence);
}
