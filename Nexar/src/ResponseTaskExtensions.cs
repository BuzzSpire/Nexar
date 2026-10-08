namespace Nexar;

/// <summary>
/// Lets a whole request be written as one chain:
/// <c>await client.Get(url).Send().ErrorForStatus().Json&lt;T&gt;()</c>.
/// </summary>
public static class ResponseTaskExtensions
{
    /// <summary>
    /// Awaits the response and throws if its status is 4xx or 5xx.
    /// </summary>
    public static async Task<NexarResponse> ErrorForStatus(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        var result = await response.ConfigureAwait(false);
        return await result.ErrorForStatus(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response, reads its body as text and disposes it.
    /// </summary>
    public static async Task<string> Text(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Text(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response, reads its body as text with <paramref name="fallback"/> for bodies without a charset, and disposes it.
    /// </summary>
    public static async Task<string> Text(this Task<NexarResponse> response, System.Text.Encoding fallback, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Text(fallback, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response, reads its body as bytes and disposes it.
    /// </summary>
    public static async Task<byte[]> Bytes(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Bytes(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response, deserializes its body as JSON and disposes it.
    /// </summary>
    public static async Task<T> Json<T>(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Json<T>(cancellationToken).ConfigureAwait(false);
    }
}
