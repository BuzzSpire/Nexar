namespace Nexar;

/// <summary>
/// What <see cref="ClientBuilder.RetryWhen"/> sees when deciding whether to retry a failed attempt.
/// </summary>
/// <param name="Attempt">The number of the retry being considered (1 for the first retry).</param>
/// <param name="Method">The request method.</param>
/// <param name="Url">The request URL.</param>
/// <param name="Response">The response, when the attempt got one (its body has not been read).</param>
/// <param name="Exception">The transport failure (timeout, connection error, ...), when the attempt got no response.</param>
/// <param name="RetriedByDefault">Whether Nexar would retry this failure on its own.</param>
public sealed record RetryContext(
    int Attempt,
    HttpMethod Method,
    Uri Url,
    HttpResponseMessage? Response,
    Exception? Exception,
    bool RetriedByDefault);

/// <summary>
/// A re-send reported to <see cref="ClientBuilder.OnRetry"/>.
/// </summary>
/// <param name="Attempt">How many times the request has been re-sent so far, including this one.</param>
/// <param name="Reason">Why: a status code such as <c>503</c>, or <c>timeout</c>, <c>connect</c>, <c>request</c>, <c>unauthorized</c>.</param>
/// <param name="Delay">How long Nexar waits before re-sending.</param>
/// <param name="Method">The request method.</param>
/// <param name="Url">The request URL.</param>
public sealed record RetryEvent(int Attempt, string Reason, TimeSpan Delay, HttpMethod Method, Uri Url);
