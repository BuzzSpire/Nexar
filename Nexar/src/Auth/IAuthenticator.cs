namespace Nexar;

/// <summary>
/// Adds credentials to outgoing requests. Set one for every request with
/// <see cref="ClientBuilder.Auth"/> or for a single request with <see cref="RequestBuilder.Auth"/>.
/// Ready-made implementations live in <see cref="Auth"/>.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe: one instance is shared by all requests of a client.
/// </remarks>
public interface IAuthenticator
{
    /// <summary>
    /// Adds credentials to <paramref name="request"/>. Called before every attempt, including retries.
    /// </summary>
    ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// Called when the server answers <c>401 Unauthorized</c>. Return <c>true</c> to send the request
    /// once more (for example after refreshing a token or reading a challenge).
    /// Only requests with a replayable body are re-sent, and at most once.
    /// </summary>
    ValueTask<bool> OnUnauthorizedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}
