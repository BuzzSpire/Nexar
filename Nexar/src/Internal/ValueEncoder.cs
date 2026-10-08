using System.Globalization;
using System.Text.Json;

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

    public static List<KeyValuePair<string, string>> ToPairs(object values, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(values);

        switch (values)
        {
            case IEnumerable<KeyValuePair<string, string>> strings:
                return strings.ToList();
            case IEnumerable<KeyValuePair<string, object?>> objects:
                return objects
                    .Select(kv => (kv.Key, Value: Format(kv.Value)))
                    .Where(kv => kv.Value != null)
                    .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!))
                    .ToList();
        }

        var element = JsonSerializer.SerializeToElement(values, values.GetType(), options);
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{values.GetType().Name} must serialize to a JSON object.", nameof(values));
        }

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                // Arrays become repeated keys: ids=1&ids=2
                foreach (var item in property.Value.EnumerateArray())
                {
                    AddScalar(pairs, property.Name, item);
                }
            }
            else
            {
                AddScalar(pairs, property.Name, property.Value);
            }
        }
        return pairs;
    }

    private static void AddScalar(List<KeyValuePair<string, string>> pairs, string name, JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new ArgumentException($"Property '{name}' is a nested {value.ValueKind}; only scalar values and arrays of scalars are supported.")
        };

        if (text != null)
        {
            pairs.Add(new(name, text));
        }
    }
}
