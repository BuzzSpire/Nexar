namespace Nexar;

/// <summary>
/// Rejects header names and values that would corrupt the request (RFC 9110 §5).
/// </summary>
internal static class HeaderValidator
{
    private const string TokenSymbols = "!#$%&'*+-.^_`|~";

    /// <exception cref="ArgumentException">The name is not a token or the value contains control characters.</exception>
    public static void Validate(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        if (name.Length == 0 || !name.All(IsTokenChar))
        {
            throw new ArgumentException($"'{Sanitize(name)}' is not a valid header name.", nameof(name));
        }

        // Never echo the value: it may be a secret, and it is what carries the injection.
        if (value.Any(IsForbiddenInValue))
        {
            throw new ArgumentException($"The value of header '{name}' contains control characters (CR, LF, NUL, ...).", nameof(value));
        }
    }

    private static bool IsTokenChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || TokenSymbols.Contains(c);

    // Visible characters, space, tab and non-ASCII (obs-text) are allowed.
    private static bool IsForbiddenInValue(char c) => (c < 0x20 && c != '\t') || c == 0x7F;

    private static string Sanitize(string name) => new(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
}
