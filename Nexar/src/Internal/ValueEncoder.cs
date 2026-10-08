using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Nexar;

/// <summary>
/// Turns values into the strings used in query strings and form bodies.
/// </summary>
internal static class ValueEncoder
{
    public static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    /// <summary>
    /// Flattens a dictionary or object into key/value pairs. Objects go through the client's JSON options,
    /// so naming policies apply; arrays and nested objects follow <paramref name="style"/>.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Object)]
    [RequiresDynamicCode(AotMessages.Object)]
    public static List<KeyValuePair<string, string>> ToPairs(object values, JsonSerializerOptions options, QueryStyle style)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values is IEnumerable<KeyValuePair<string, string>> strings)
        {
            return strings.ToList();
        }

        var root = JsonSerializer.SerializeToElement(values, values.GetType(), options);
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{values.GetType().Name} must serialize to a JSON object.", nameof(values));
        }

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var property in root.EnumerateObject())
        {
            Encode(pairs, property.Name, property.Value, style);
        }
        return pairs;
    }

    private static void Encode(List<KeyValuePair<string, string>> pairs, string key, JsonElement value, QueryStyle style)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (style.Nested == NestedStyle.Reject)
                {
                    throw new ArgumentException($"'{key}' is a nested object; set NestedStyle.Brackets or NestedStyle.Dot to encode it.");
                }
                foreach (var property in value.EnumerateObject())
                {
                    var childKey = style.Nested == NestedStyle.Dot ? $"{key}.{property.Name}" : $"{key}[{property.Name}]";
                    Encode(pairs, childKey, property.Value, style);
                }
                break;

            case JsonValueKind.Array:
                EncodeArray(pairs, key, value, style);
                break;

            default:
                if (Scalar(value) is { } text)
                {
                    pairs.Add(new(key, text));
                }
                break;
        }
    }

    private static void EncodeArray(List<KeyValuePair<string, string>> pairs, string key, JsonElement array, QueryStyle style)
    {
        var items = array.EnumerateArray().ToList();
        var hasComplexItems = items.Any(i => i.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
        if (hasComplexItems && (style.Nested == NestedStyle.Reject || style.Arrays is ArrayStyle.Comma or ArrayStyle.Repeat))
        {
            throw new ArgumentException(
                $"'{key}' contains objects or arrays; use ArrayStyle.Index or ArrayStyle.Brackets with NestedStyle.Brackets or NestedStyle.Dot.");
        }

        switch (style.Arrays)
        {
            case ArrayStyle.Comma:
                var joined = string.Join(",", items.Select(Scalar).Where(t => t != null));
                if (joined.Length > 0)
                {
                    pairs.Add(new(key, joined));
                }
                break;

            case ArrayStyle.Brackets:
                foreach (var item in items)
                {
                    Encode(pairs, $"{key}[]", item, style);
                }
                break;

            case ArrayStyle.Index:
                for (var i = 0; i < items.Count; i++)
                {
                    Encode(pairs, $"{key}[{i}]", items[i], style);
                }
                break;

            default:
                foreach (var item in items)
                {
                    Encode(pairs, key, item, style);
                }
                break;
        }
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
}
