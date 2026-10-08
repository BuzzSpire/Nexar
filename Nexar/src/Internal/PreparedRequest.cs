namespace Nexar;

/// <summary>
/// A fully built request that can produce a fresh <see cref="HttpRequestMessage"/> for every attempt.
/// </summary>
internal sealed record PreparedRequest
{
    public required HttpMethod Method { get; init; }

    public required Uri Url { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; }

    /// <summary>Creates the body for one attempt, or null for no body.</summary>
    public Func<HttpContent>? Content { get; init; }

    /// <summary>Whether the body can be created again for a retry.</summary>
    public bool IsReplayable { get; init; } = true;

    /// <summary>Whether timeouts and transient statuses may be retried.</summary>
    public bool IsIdempotent { get; init; }

    public IAuthenticator? Authenticator { get; init; }

    public TimeSpan? Timeout { get; init; }

    public Version? Version { get; init; }

    public HttpVersionPolicy? VersionPolicy { get; init; }

    public bool ExpectContinue { get; init; }

    /// <summary>The largest body Text()/Bytes()/Json()/SaveTo() may read, or null for no limit.</summary>
    public long? MaxResponseSize { get; init; }

    public IProgress<TransferProgress>? DownloadProgress { get; init; }

    public HttpRequestMessage CreateMessage()
    {
        var message = new HttpRequestMessage(Method, Url);
        if (Version != null)
        {
            message.Version = Version;
        }
        if (VersionPolicy != null)
        {
            message.VersionPolicy = VersionPolicy.Value;
        }

        if (Content != null)
        {
            message.Content = Content();
            if (ExpectContinue)
            {
                message.Headers.ExpectContinue = true;
            }
        }

        foreach (var (name, values) in Headers)
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
