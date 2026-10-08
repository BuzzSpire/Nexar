namespace Nexar.Examples;

/// <summary>
/// Helpers shared by the examples. Code between "// snippet:name" and "// end-snippet" is copied
/// verbatim into README.md, and a test keeps the two in sync.
/// </summary>
public static class Example
{
    public static void Expect(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Expectation failed: {what}");
        }
    }
}
