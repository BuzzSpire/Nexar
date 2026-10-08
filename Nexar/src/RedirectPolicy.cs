namespace Nexar;

/// <summary>
/// How a client follows redirects. Set with <see cref="ClientBuilder.Redirects"/>.
/// </summary>
public sealed class RedirectPolicy
{
    private RedirectPolicy(int maxRedirects)
    {
        MaxRedirects = maxRedirects;
    }

    /// <summary>Follow up to 10 redirects (the default).</summary>
    public static RedirectPolicy Default { get; } = new(10);

    /// <summary>Never follow redirects; the 3xx response is returned as-is.</summary>
    public static RedirectPolicy None { get; } = new(0);

    /// <summary>Follow up to <paramref name="maxRedirects"/> redirects.</summary>
    public static RedirectPolicy Limited(int maxRedirects)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRedirects);
        return maxRedirects == 0 ? None : new RedirectPolicy(maxRedirects);
    }

    /// <summary>The maximum number of redirects to follow; 0 means none.</summary>
    public int MaxRedirects { get; }
}
