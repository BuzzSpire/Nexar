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
    /// Awaits the response and returns its body as an unbuffered stream.
    /// Disposing the stream disposes the response and releases the connection.
    /// </summary>
    /// <example>
    /// <code>
    /// await using var body = await client.Get("/big.zip").Send().ErrorForStatus().Stream();
    /// await body.CopyToAsync(file);
    /// </code>
    /// </example>
    public static async Task<Stream> Stream(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        var result = await response.ConfigureAwait(false);
        try
        {
            var body = await result.Stream(cancellationToken).ConfigureAwait(false);
            return new OwningStream(body, result);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Awaits the response, saves its body to <paramref name="path"/> (atomically) and disposes it.
    /// </summary>
    public static async Task SaveTo(this Task<NexarResponse> response, string path, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        await result.SaveTo(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response and reads it as Server-Sent Events. The response is disposed when the enumeration ends.
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var e in client.Post("/v1/chat").Json(request).Send().ErrorForStatus().Events(ct))
    ///     Console.Write(e.Data);
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<ServerSentEvent> Events(this Task<NexarResponse> response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        await foreach (var item in result.Events(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Awaits the response and reads it as newline-delimited JSON. The response is disposed when the enumeration ends.
    /// </summary>
    public static async IAsyncEnumerable<T> JsonLines<T>(this Task<NexarResponse> response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        await foreach (var item in result.JsonLines<T>(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Awaits the response and reads its JSON array body one element at a time. The response is disposed when the enumeration ends.
    /// </summary>
    public static async IAsyncEnumerable<T> JsonStream<T>(this Task<NexarResponse> response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        await foreach (var item in result.JsonStream<T>(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
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
