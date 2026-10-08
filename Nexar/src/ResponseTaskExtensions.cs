using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

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
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public static IAsyncEnumerable<T> JsonLines<T>(this Task<NexarResponse> response, CancellationToken cancellationToken = default) =>
        Drain(response, (r, ct) => r.JsonLines<T>(ct), cancellationToken);

    /// <summary>
    /// Like <see cref="JsonLines{T}(Task{NexarResponse}, CancellationToken)"/>, with source-generated metadata.
    /// </summary>
    public static IAsyncEnumerable<T> JsonLines<T>(this Task<NexarResponse> response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default) =>
        Drain(response, (r, ct) => r.JsonLines(typeInfo, ct), cancellationToken);

    /// <summary>
    /// Awaits the response and reads its JSON array body one element at a time. The response is disposed when the enumeration ends.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public static IAsyncEnumerable<T> JsonStream<T>(this Task<NexarResponse> response, CancellationToken cancellationToken = default) =>
        Drain(response, (r, ct) => r.JsonStream<T>(ct), cancellationToken);

    /// <summary>
    /// Like <see cref="JsonStream{T}(Task{NexarResponse}, CancellationToken)"/>, with source-generated metadata.
    /// </summary>
    public static IAsyncEnumerable<T> JsonStream<T>(this Task<NexarResponse> response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default) =>
        Drain(response, (r, ct) => r.JsonStream(typeInfo, ct), cancellationToken);

    /// <summary>
    /// Awaits the response, deserializes its body as JSON and disposes it.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public static async Task<T> Json<T>(this Task<NexarResponse> response, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Json<T>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits the response, deserializes its body with source-generated metadata and disposes it, e.g.
    /// <c>await client.Get(url).Send().Json(AppJsonContext.Default.User)</c>. Safe for trimming and Native AOT.
    /// </summary>
    public static async Task<T> Json<T>(this Task<NexarResponse> response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        using var result = await response.ConfigureAwait(false);
        return await result.Json(typeInfo, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> Drain<T>(Task<NexarResponse> response, Func<NexarResponse, CancellationToken, IAsyncEnumerable<T>> read,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var result = await response.ConfigureAwait(false);
        await foreach (var item in read(result, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
