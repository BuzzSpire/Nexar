using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Nexar;

/// <summary>
/// An RFC 9457 problem document (<c>application/problem+json</c>), available on
/// <see cref="NexarException.Problem"/> after <c>ErrorForStatus()</c>.
/// </summary>
public sealed class NexarProblemDetails
{
    private static readonly HashSet<string> StandardMembers = new(StringComparer.Ordinal)
    {
        "type", "title", "status", "detail", "instance"
    };

    /// <summary>A URI identifying the problem type. Defaults to <c>about:blank</c>.</summary>
    public string Type { get; init; } = "about:blank";

    /// <summary>A short, human-readable summary of the problem type.</summary>
    public string? Title { get; init; }

    /// <summary>The HTTP status code the server generated for this occurrence.</summary>
    public int? Status { get; init; }

    /// <summary>A human-readable explanation of this occurrence.</summary>
    public string? Detail { get; init; }

    /// <summary>A URI identifying this occurrence.</summary>
    public string? Instance { get; init; }

    /// <summary>All other members, e.g. <c>errors</c> or <c>traceId</c>.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>
    /// Deserializes an extension member, e.g. <c>problem.Extension&lt;Dictionary&lt;string, string[]&gt;&gt;("errors")</c>.
    /// Returns <c>default</c> if it is missing or has a different shape.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public T? Extension<T>(string name, JsonSerializerOptions? options = null) =>
        ExtensionCore(name, element => element.Deserialize<T>(options));

    /// <summary>
    /// Deserializes an extension member with source-generated metadata. Safe for trimming and Native AOT.
    /// </summary>
    public T? Extension<T>(string name, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return ExtensionCore(name, element => element.Deserialize(typeInfo));
    }

    private T? ExtensionCore<T>(string name, Func<JsonElement, T?> deserialize)
    {
        if (!Extensions.TryGetValue(name, out var element))
        {
            return default;
        }
        try
        {
            return deserialize(element);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    internal static bool IsProblemMediaType(string? mediaType) =>
        string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase);

    internal static NexarProblemDetails? TryParse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // RFC 9457 §3.1: members with the wrong type are ignored.
            string? GetString(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            return new NexarProblemDetails
            {
                Type = GetString("type") ?? "about:blank",
                Title = GetString("title"),
                Status = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var code) ? code : null,
                Detail = GetString("detail"),
                Instance = GetString("instance"),
                Extensions = root.EnumerateObject()
                    .Where(p => !StandardMembers.Contains(p.Name))
                    .ToDictionary(p => p.Name, p => p.Value.Clone())
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
