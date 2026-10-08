namespace Nexar;

/// <summary>
/// A fully built request that can produce a fresh <see cref="HttpRequestMessage"/> for every attempt.
/// </summary>
internal sealed class PreparedRequest(
    HttpMethod method,
    Uri url,
    IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
    Func<HttpContent>? content,
    bool isReplayable,
    TimeSpan? timeout,
    Version? version,
    IAuthenticator? authenticator,
    bool isIdempotent)
{
    public HttpMethod Method { get; } = method;

    public Uri Url { get; } = url;

    /// <summary>Whether timeouts and transient statuses may be retried.</summary>
    public bool IsIdempotent { get; } = isIdempotent;

    public IAuthenticator? Authenticator { get; } = authenticator;

    public bool IsReplayable { get; } = isReplayable;

    public TimeSpan? Timeout { get; } = timeout;

    public HttpRequestMessage CreateMessage()
    {
        var message = new HttpRequestMessage(Method, Url);
        if (version != null)
        {
            message.Version = version;
        }

        if (content != null)
        {
            message.Content = content();
        }

        foreach (var (name, values) in headers)
        {
            if (message.Headers.TryAddWithoutValidation(name, values))
            {
                continue;
            }

            // Content-* headers belong to the body; they replace what the body set itself.
            if (message.Content != null)
            {
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, values);
            }
        }

        return message;
    }
}
