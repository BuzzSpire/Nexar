namespace Nexar;

/// <summary>
/// How arrays are written in query strings and form bodies.
/// </summary>
public enum ArrayStyle
{
    /// <summary><c>ids=1&amp;ids=2</c> (the default)</summary>
    Repeat,

    /// <summary><c>ids[]=1&amp;ids[]=2</c> (PHP, Rails)</summary>
    Brackets,

    /// <summary><c>ids=1,2</c></summary>
    Comma,

    /// <summary><c>ids[0]=1&amp;ids[1]=2</c></summary>
    Index
}

/// <summary>
/// How nested objects are written in query strings and form bodies.
/// </summary>
public enum NestedStyle
{
    /// <summary>Nested objects are an error (the default).</summary>
    Reject,

    /// <summary><c>filter[status]=open</c></summary>
    Brackets,

    /// <summary><c>filter.status=open</c></summary>
    Dot
}

/// <summary>
/// How <c>Query(object)</c> and <c>Form(object)</c> encode arrays and nested objects.
/// Set a client default with <see cref="ClientBuilder.QueryStyle"/>.
/// </summary>
/// <remarks>
/// A struct, so <c>Query("key", null)</c> stays unambiguous; <c>default</c> equals <see cref="Default"/>.
/// </remarks>
public readonly record struct QueryStyle(ArrayStyle Arrays = ArrayStyle.Repeat, NestedStyle Nested = NestedStyle.Reject)
{
    /// <summary>Repeated keys for arrays; nested objects rejected.</summary>
    public static QueryStyle Default { get; } = new();
}
