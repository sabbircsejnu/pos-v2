using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

public class ProductIdentifierService : IProductIdentifierService
{
    private static readonly Regex AlphaNumRegex = new("[^A-Z0-9]", RegexOptions.Compiled);
    private readonly IProductVariantRepository _variantRepository;
    private readonly RetailPOSDbContext _context;

    public ProductIdentifierService(IProductVariantRepository variantRepository, RetailPOSDbContext context)
    {
        _variantRepository = variantRepository;
        _context = context;
    }

    public async Task<string> ResolveVariantSkuAsync(
        string? requestedSku,
        string mainProductCode,
        string? sizeCode,
        string? attributeCode,
        long? excludeVariantId = null)
    {
        if (string.IsNullOrWhiteSpace(mainProductCode))
            throw new InvalidOperationException("Main Product Code is required to generate variant SKU.");

        if (!string.IsNullOrWhiteSpace(requestedSku))
        {
            var normalized = NormalizeSkuToken(requestedSku, 50);
            if (await _variantRepository.SkuExistsAsync(normalized, excludeVariantId))
                throw new InvalidOperationException($"Variant SKU '{normalized}' already exists");
            return normalized;
        }

        var baseMain = NormalizeSkuToken(mainProductCode, 30);
        var size = NormalizeCode(sizeCode, fallback: "STD");
        var attr = NormalizeCode(attributeCode, fallback: "GEN");

        var baseSku = $"{baseMain}-{size}-{attr}";
        var candidate = Truncate(baseSku, 50);

        var suffix = 2;
        while (await _variantRepository.SkuExistsAsync(candidate, excludeVariantId))
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
        long? excludeVariantId = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedBarcode))
        {
            var normalized = NormalizeBarcode(requestedBarcode);
            if (await _variantRepository.BarcodeExistsAsync(normalized, excludeVariantId))
                throw new InvalidOperationException($"Variant barcode '{normalized}' already exists");
            return normalized;
        }

        var categoryCode = ((int)Math.Abs(categoryId % 1000)).ToString("D3");
        var prefix = $"880{categoryCode}";

        var existingBarcodes = await _context.ProductVariants
            .Where(v => v.Barcode != null && v.Barcode.StartsWith(prefix) && v.Barcode.Length == 13)
            .Select(v => v.Barcode!)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var barcode in existingBarcodes)
        {
            if (barcode.All(char.IsDigit) && int.TryParse(barcode.Substring(6, 6), out var seq))
                maxSeq = Math.Max(maxSeq, seq);
        }

        var nextSeq = maxSeq + 1;
        while (true)
        {
            var base12 = $"{prefix}{nextSeq:D6}";
            var full13 = base12 + CalculateEan13CheckDigit(base12);

            if (!await _variantRepository.BarcodeExistsAsync(full13, excludeVariantId))
                return full13;

            nextSeq++;
        }
    }

    public (string SizeCode, string AttributeCode) ExtractCodesFromVariantText(string? variantName, string? attributesJson)
    {
        var sizeCode = "STD";
        var attrCode = "GEN";

        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(attributesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var key = prop.Name.ToLowerInvariant();
                        var value = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();

                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        if (key.Contains("size"))
                            sizeCode = NormalizeCode(value, fallback: sizeCode);
                        else if (key.Contains("color") || key.Contains("colour") || attrCode == "GEN")
                            attrCode = NormalizeCode(value, fallback: attrCode);
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
                    sizeCode = NormalizeCode(parts.Last(), fallback: sizeCode);
                if (attrCode == "GEN")
                    attrCode = NormalizeCode(parts.First(), fallback: attrCode);
            }
        }

        return (sizeCode, attrCode);
    }

    public (string SizeCode, string AttributeCode) ExtractCodesFromOptions(IEnumerable<(string VariationName, string OptionName)> options)
    {
        var sizeCode = "STD";
        var attrCode = "GEN";

        foreach (var (variationName, optionName) in options)
        {
            if (string.IsNullOrWhiteSpace(optionName))
                continue;

            var variation = variationName?.ToLowerInvariant() ?? string.Empty;
            if (variation.Contains("size"))
            {
                sizeCode = NormalizeCode(optionName, fallback: sizeCode);
            }
            else if (variation.Contains("color") || variation.Contains("colour") || attrCode == "GEN")
            {
                attrCode = NormalizeCode(optionName, fallback: attrCode);
            }
        }

        return (sizeCode, attrCode);
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
            return fallback;

        var upper = raw.Trim().ToUpperInvariant();
        var compact = AlphaNumRegex.Replace(upper, string.Empty);

        if (string.IsNullOrWhiteSpace(compact))
            return fallback;

        if (compact.All(char.IsDigit))
            return compact.Length <= 4 ? compact : compact[..4];

        return compact.Length <= 3 ? compact : compact[..3];
    }

    private static string NormalizeBarcode(string input)
    {
        var digitsOnly = new string(input.Where(char.IsDigit).ToArray());

        if (digitsOnly.Length == 12)
            return digitsOnly + CalculateEan13CheckDigit(digitsOnly);

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
            throw new InvalidOperationException("EAN-13 base value must be exactly 12 digits.");

        var sum = 0;
        for (var i = 0; i < base12.Length; i++)
        {
            var digit = base12[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength];
    }
}
