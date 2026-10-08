namespace Nexar;

/// <summary>
/// Messages for members that use reflection-based System.Text.Json.
/// </summary>
internal static class AotMessages
{
    public const string Json =
        "Reflection-based JSON serialization may need types that trimming removes. " +
        "For trimmed or Native AOT apps, use the overload that takes a JsonTypeInfo<T> from a JsonSerializerContext.";

    public const string Object =
        "Objects are flattened with reflection-based JSON serialization, which may need types that trimming removes. " +
        "For trimmed or Native AOT apps, pass key/value pairs instead.";
}
