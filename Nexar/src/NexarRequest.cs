namespace Nexar;

/// <summary>
/// A fully built request that has not been sent yet, made by <see cref="RequestBuilder.Build"/>.
/// Inspect or change it (for example to sign it), then send it with <see cref="NexarClient.Execute"/>.
/// </summary>
/// <example>
/// <code>
/// var request = client.Post("/orders").Json(order).Build();
/// var body = await request.ReadBodyAsync();
/// request.SetHeader("X-Signature", Sign(request.Method, request.Url, body));
/// using var response = await client.Execute(request);
/// </code>
/// </example>
public sealed class NexarRequest
{
    private readonly PreparedRequest _prepared;
    private readonly Dictionary<string, List<string>> _headers;

    internal NexarRequest(PreparedRequest prepared)
    {
        _prepared = prepared;
        _headers = prepared.Headers.ToDictionary(h => h.Key, h => h.Value.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method => _prepared.Method;

    /// <summary>The final URL, with the base URL, path parameters and query applied.</summary>
    public Uri Url => _prepared.Url;

    /// <summary>The request timeout, if one was set on the request.</summary>
    public TimeSpan? Timeout => _prepared.Timeout;

    /// <summary>True if the request has a body.</summary>
    public bool HasBody => _prepared.Content != null;

    /// <summary>
    /// The request headers, default headers included. Authentication headers are added later, at send time,
    /// and <c>Content-*</c> headers that the body sets itself are not listed.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers =>
        _headers.ToDictionary(h => h.Key, h => (IReadOnlyList<string>)h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    /// <summary>A header's value, with multiple values joined by <c>", "</c>; null if missing.</summary>
    public string? Header(string name) => _headers.TryGetValue(name, out var values) ? string.Join(", ", values) : null;

    /// <summary>Sets a header, replacing any previous value.</summary>
    /// <exception cref="ArgumentException">The name or value is invalid.</exception>
    public NexarRequest SetHeader(string name, string value)
    {
        HeaderValidator.Validate(name, value);
        _headers[name] = [value];
        return this;
    }

    /// <summary>Removes a header.</summary>
    public NexarRequest RemoveHeader(string name)
    {
        _headers.Remove(name);
        return this;
    }

    /// <summary>
    /// The body's <c>Content-Type</c>, or null if there is no body or it is a stream.
    /// </summary>
    public string? ContentType
    {
        get
        {
            if (_prepared.Content == null || !_prepared.IsReplayable)
            {
                return null;
            }
            using var content = _prepared.Content();
            return content.Headers.ContentType?.ToString();
        }
    }

    /// <summary>
    /// The body bytes as they will be sent (after compression, if any), or null if there is no body.
    /// </summary>
    /// <exception cref="InvalidOperationException">The body is a stream, which cannot be read without consuming it.</exception>
    public async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken = default)
    {
        if (_prepared.Content == null)
        {
            return null;
        }
        if (!_prepared.IsReplayable)
        {
            throw new InvalidOperationException("A stream body cannot be read without consuming it.");
        }
        using var content = _prepared.Content();
        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    internal PreparedRequest ToPrepared() => _prepared with
    {
        Headers = _headers.ToDictionary(h => h.Key, h => (IReadOnlyList<string>)h.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
    };
}
