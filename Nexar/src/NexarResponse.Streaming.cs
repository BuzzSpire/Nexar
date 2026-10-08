using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Nexar;

public sealed partial class NexarResponse
{
    private IReadOnlyDictionary<string, Uri>? _links;

    /// <summary>
    /// Links from the <c>Link</c> header (RFC 8288) by relation type, resolved against <see cref="Url"/>,
    /// e.g. <c>res.Links["next"]</c>. The first link wins when a relation appears twice.
    /// </summary>
    public IReadOnlyDictionary<string, Uri> Links => _links ??= LinkHeader.Parse(Header("Link"), Url);

    /// <summary>
    /// Reads a <c>text/event-stream</c> body as Server-Sent Events, following the WHATWG parsing rules.
    /// The body is not buffered and the client timeout does not apply; use the cancellation token.
    /// </summary>
    public async IAsyncEnumerable<ServerSentEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = await OpenTextReaderAsync(cancellationToken).ConfigureAwait(false);
        string? eventType = null;
        string? lastEventId = null;
        TimeSpan? retry = null;
        var data = new StringBuilder();
        var hasData = false;

        while (await ReadLineAsync(reader, cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                // A blank line dispatches the event; an event without data is dropped.
                if (hasData)
                {
                    if (data.Length > 0 && data[^1] == '\n')
                    {
                        data.Length--;
                    }
                    yield return new ServerSentEvent(string.IsNullOrEmpty(eventType) ? "message" : eventType, data.ToString(), lastEventId, retry);
                }
                eventType = null;
                retry = null;
                data.Clear();
                hasData = false;
                continue;
            }

            if (line[0] == ':')
            {
                continue;   // comment
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    eventType = value;
                    break;
                case "data":
                    data.Append(value).Append('\n');
                    hasData = true;
                    break;
                case "id" when !value.Contains('\0'):
                    lastEventId = value;
                    break;
                case "retry" when value.Length > 0 && value.All(char.IsAsciiDigit) && long.TryParse(value, out var milliseconds):
                    retry = TimeSpan.FromMilliseconds(milliseconds);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a newline-delimited JSON body (NDJSON / JSON Lines) one item at a time, with the client's JSON options.
    /// Blank lines are skipped. The body is not buffered and the client timeout does not apply.
    /// </summary>
    /// <exception cref="NexarException">A line is not valid JSON for <typeparamref name="T"/> (<see cref="ErrorKind.Decode"/>).</exception>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public IAsyncEnumerable<T> JsonLines<T>(CancellationToken cancellationToken = default) =>
        JsonLinesCore(line => JsonSerializer.Deserialize<T>(line, _jsonOptions), cancellationToken);

    /// <summary>
    /// Reads newline-delimited JSON with source-generated metadata. Safe for trimming and Native AOT.
    /// </summary>
    public IAsyncEnumerable<T> JsonLines<T>(JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return JsonLinesCore(line => JsonSerializer.Deserialize(line, typeInfo), cancellationToken);
    }

    private async IAsyncEnumerable<T> JsonLinesCore<T>(Func<string, T?> deserialize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = await OpenTextReaderAsync(cancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        while (await ReadLineAsync(reader, cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            T? item;
            try
            {
                item = deserialize(line);
            }
            catch (JsonException ex)
            {
                throw new NexarException(ErrorKind.Decode, $"Cannot decode line {lineNumber} as {typeof(T).Name}: {ex.Message}", Url, innerException: ex);
            }
            yield return item!;
        }
    }

    /// <summary>
    /// Reads a JSON array body one element at a time, so very large arrays are never held in memory.
    /// The client timeout does not apply; use the cancellation token.
    /// </summary>
    /// <exception cref="NexarException">The body is not a JSON array of <typeparamref name="T"/> (<see cref="ErrorKind.Decode"/>).</exception>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public IAsyncEnumerable<T> JsonStream<T>(CancellationToken cancellationToken = default) =>
        JsonStreamCore((body, ct) => JsonSerializer.DeserializeAsyncEnumerable<T>(body, _jsonOptions, ct), cancellationToken);

    /// <summary>
    /// Reads a JSON array body one element at a time with source-generated metadata. Safe for trimming and Native AOT.
    /// </summary>
    public IAsyncEnumerable<T> JsonStream<T>(JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return JsonStreamCore((body, ct) => JsonSerializer.DeserializeAsyncEnumerable(body, typeInfo, ct), cancellationToken);
    }

    private async IAsyncEnumerable<T> JsonStreamCore<T>(Func<Stream, CancellationToken, IAsyncEnumerable<T?>> deserialize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var body = await Stream(cancellationToken).ConfigureAwait(false);
        await using var items = deserialize(body, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var index = 0;
        while (true)
        {
            T? item;
            try
            {
                if (!await items.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }
                item = items.Current;
            }
            catch (JsonException ex)
            {
                throw new NexarException(ErrorKind.Decode, $"Cannot decode element {index} as {typeof(T).Name}: {ex.Message}", Url, innerException: ex);
            }
            catch (HttpIOException ex)
            {
                throw new NexarException(ErrorKind.Body, $"Failed to read the response body: {ex.Message}", Url, innerException: ex);
            }
            index++;
            yield return item!;
        }
    }

    private async Task<StreamReader> OpenTextReaderAsync(CancellationToken cancellationToken)
    {
        var body = await Stream(cancellationToken).ConfigureAwait(false);
        return new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    private async ValueTask<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpIOException ex)
        {
            throw new NexarException(ErrorKind.Body, $"Failed to read the response body: {ex.Message}", Url, innerException: ex);
        }
    }
}

/// <summary>
/// Parses RFC 8288 <c>Link</c> headers: <c>&lt;https://api/x?page=2&gt;; rel="next", &lt;...&gt;; rel="last"</c>.
/// </summary>
internal static class LinkHeader
{
    public static IReadOnlyDictionary<string, Uri> Parse(string? header, Uri baseUrl)
    {
        var links = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(header))
        {
            return links;
        }

        var i = 0;
        while (i < header.Length)
        {
            var start = header.IndexOf('<', i);
            if (start < 0)
            {
                break;
            }
            var end = header.IndexOf('>', start);
            if (end < 0)
            {
                break;
            }
            var target = header[(start + 1)..end];

            // Parameters run until the next comma that is not inside quotes.
            var j = end + 1;
            var inQuotes = false;
            while (j < header.Length && (inQuotes || header[j] != ','))
            {
                if (header[j] == '"')
                {
                    inQuotes = !inQuotes;
                }
                j++;
            }
            var parameters = header[(end + 1)..j];
            i = j + 1;

            if (!Uri.TryCreate(baseUrl, target.Trim(), out var uri))
            {
                continue;
            }
            foreach (var rel in Relations(parameters))
            {
                links.TryAdd(rel, uri);
            }
        }
        return links;
    }

    private static IEnumerable<string> Relations(string parameters)
    {
        foreach (var parameter in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = parameter.IndexOf('=');
            if (equals < 0 || !parameter[..equals].Trim().Equals("rel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var value = parameter[(equals + 1)..].Trim().Trim('"');
            foreach (var rel in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return rel;
            }
        }
    }
}
