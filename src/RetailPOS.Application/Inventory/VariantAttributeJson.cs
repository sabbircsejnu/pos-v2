using System.Text.Json;

namespace RetailPOS.Application.Inventory;

public static class VariantAttributeJson
{
    public static Dictionary<string, List<string>> Parse(string? attributesJson)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(attributesJson))
            return result;

        try
        {
            using var document = JsonDocument.Parse(attributesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var values = ExtractValues(property.Value);
                if (values.Count > 0)
                    result[property.Name] = values;
            }
        }
        catch (JsonException)
        {
            // Non-JSON payloads are ignored by the existing structured filters.
        }

        return result;
    }

    public static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

    private static List<string> ExtractValues(JsonElement element)
    {
        var values = new List<string>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = ToStringValue(item);
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }

        var singleValue = ToStringValue(element);
        if (!string.IsNullOrWhiteSpace(singleValue))
            values.Add(singleValue);

        return values;
    }

    private static string ToStringValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
}
