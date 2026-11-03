using System.Text.Json;
using System.Text.RegularExpressions;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Services;

public sealed class ProductIdentifierResolver(IProductIdentifierLookupStore lookupStore)
    : IProductIdentifierResolver
{
    private static readonly Regex AlphaNumRegex = new("[^A-Z0-9]", RegexOptions.Compiled);

    public async Task<string> ResolveVariantSkuAsync(
        string? requestedSku,
        string mainProductCode,
        string? sizeCode,
        string? attributeCode,
        long? excludeVariantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mainProductCode))
        {
            throw new InvalidOperationException("Main Product Code is required to generate variant SKU.");
        }

        if (!string.IsNullOrWhiteSpace(requestedSku))
        {
            var normalized = NormalizeSkuToken(requestedSku, 50);
            if (await lookupStore.SkuExistsAsync(normalized, excludeVariantId, cancellationToken))
            {
                throw new InvalidOperationException($"Variant SKU '{normalized}' already exists");
            }

            return normalized;
        }

        var baseMain = NormalizeSkuToken(mainProductCode, 30);
        var size = NormalizeCode(sizeCode, fallback: "STD");
        var attribute = NormalizeCode(attributeCode, fallback: "GEN");
        var baseSku = $"{baseMain}-{size}-{attribute}";
        var candidate = Truncate(baseSku, 50);

        var suffix = 2;
        while (await lookupStore.SkuExistsAsync(candidate, excludeVariantId, cancellationToken))
        {
            var suffixText = $"-{suffix}";
            candidate = Truncate(baseSku, 50 - suffixText.Length) + suffixText;
            suffix++;
        }

        return candidate;
    }

    public async Task<string> ResolveVariantBarcodeAsync(
        string? requestedBarcode,
        long categoryId,
        long? excludeVariantId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedBarcode))
        {
            var normalized = NormalizeBarcode(requestedBarcode);
            if (await lookupStore.BarcodeExistsAsync(normalized, excludeVariantId, cancellationToken))
            {
                throw new InvalidOperationException($"Variant barcode '{normalized}' already exists");
            }

            return normalized;
        }

        var categoryCode = ((int)Math.Abs(categoryId % 1000)).ToString("D3");
        var prefix = $"880{categoryCode}";
        var existingBarcodes = await lookupStore.GetBarcodeCandidatesAsync(prefix, cancellationToken);

        var maxSequence = 0;
        foreach (var barcode in existingBarcodes)
        {
            if (barcode.All(char.IsDigit) && int.TryParse(barcode.Substring(6, 6), out var sequence))
            {
                maxSequence = Math.Max(maxSequence, sequence);
            }
        }

        var nextSequence = maxSequence + 1;
        while (true)
        {
            var base12 = $"{prefix}{nextSequence:D6}";
            var full13 = base12 + CalculateEan13CheckDigit(base12);
            if (!await lookupStore.BarcodeExistsAsync(full13, excludeVariantId, cancellationToken))
            {
                return full13;
            }

            nextSequence++;
        }
    }

    public (string SizeCode, string AttributeCode) ExtractCodesFromVariantText(
        string? variantName,
        string? attributesJson)
    {
        var sizeCode = "STD";
        var attributeCode = "GEN";

        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            try
            {
                using var document = JsonDocument.Parse(attributesJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var key = property.Name.ToLowerInvariant();
                        var value = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();

                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (key.Contains("size"))
                        {
                            sizeCode = NormalizeCode(value, fallback: sizeCode);
                        }
                        else if (key.Contains("color") || key.Contains("colour") || attributeCode == "GEN")
                        {
                            attributeCode = NormalizeCode(value, fallback: attributeCode);
                        }
                    }
                }
            }
            catch
            {
                // Non-JSON or malformed attributes are ignored.
            }
        }

        if (!string.IsNullOrWhiteSpace(variantName))
        {
            var parts = variantName.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (sizeCode == "STD")
                {
                    sizeCode = NormalizeCode(parts.Last(), fallback: sizeCode);
                }

                if (attributeCode == "GEN")
                {
                    attributeCode = NormalizeCode(parts.First(), fallback: attributeCode);
                }
            }
        }

        return (sizeCode, attributeCode);
    }

    public (string SizeCode, string AttributeCode) ExtractCodesFromOptions(
        IEnumerable<(string VariationName, string OptionName)> options)
    {
        var sizeCode = "STD";
        var attributeCode = "GEN";

        foreach (var (variationName, optionName) in options)
        {
            if (string.IsNullOrWhiteSpace(optionName))
            {
                continue;
            }

            var variation = variationName?.ToLowerInvariant() ?? string.Empty;
            if (variation.Contains("size"))
            {
                sizeCode = NormalizeCode(optionName, fallback: sizeCode);
            }
            else if (variation.Contains("color") || variation.Contains("colour") || attributeCode == "GEN")
            {
                attributeCode = NormalizeCode(optionName, fallback: attributeCode);
            }
        }

        return (sizeCode, attributeCode);
    }

    private static string NormalizeSkuToken(string value, int maxLength)
    {
        var upper = value.Trim().ToUpperInvariant();
        upper = upper.Replace(' ', '-');
        upper = Regex.Replace(upper, "-+", "-");
        return Truncate(upper, maxLength);
    }

    private static string NormalizeCode(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var upper = raw.Trim().ToUpperInvariant();
        var compact = AlphaNumRegex.Replace(upper, string.Empty);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return fallback;
        }

        if (compact.All(char.IsDigit))
        {
            return compact.Length <= 4 ? compact : compact[..4];
        }

        return compact.Length <= 3 ? compact : compact[..3];
    }

    private static string NormalizeBarcode(string input)
    {
        var digitsOnly = new string(input.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length == 12)
        {
            return digitsOnly + CalculateEan13CheckDigit(digitsOnly);
        }

        if (digitsOnly.Length == 13)
        {
            var expected = CalculateEan13CheckDigit(digitsOnly[..12]);
            return digitsOnly[..12] + expected;
        }

        throw new InvalidOperationException("Barcode must be 12 or 13 numeric digits.");
    }

    private static int CalculateEan13CheckDigit(string base12)
    {
        if (base12.Length != 12 || !base12.All(char.IsDigit))
        {
            throw new InvalidOperationException("EAN-13 base value must be exactly 12 digits.");
        }

        var sum = 0;
        for (var index = 0; index < base12.Length; index++)
        {
            var digit = base12[index] - '0';
            sum += index % 2 == 0 ? digit : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
