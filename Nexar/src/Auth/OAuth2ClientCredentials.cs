using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Nexar;

/// <summary>
/// Configuration for <see cref="Auth.OAuth2ClientCredentials(OAuth2ClientCredentialsOptions)"/>.
/// </summary>
public sealed class OAuth2ClientCredentialsOptions
{
    /// <summary>The absolute URL of the token endpoint.</summary>
    public required string TokenUrl { get; init; }

    /// <summary>The client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>The client secret.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>Space-separated scopes to request.</summary>
    public string? Scope { get; init; }

    /// <summary>Extra form fields for the token request, e.g. <c>audience</c>.</summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>
    /// Sends <c>client_id</c>/<c>client_secret</c> in the form body (<c>client_secret_post</c>)
    /// instead of a Basic <c>Authorization</c> header (<c>client_secret_basic</c>, the default).
    /// </summary>
    public bool SendCredentialsInBody { get; init; }

    /// <summary>How long before expiry a cached token is replaced. Defaults to 30 seconds.</summary>
    public TimeSpan RefreshBeforeExpiry { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The client used to call the token endpoint. Defaults to a shared client with default settings.</summary>
    public NexarClient? TokenClient { get; init; }

    /// <summary>The clock used for token expiry. Useful in tests.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

internal sealed class OAuth2ClientCredentialsAuthenticator(OAuth2ClientCredentialsOptions options) : IAuthenticator
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile CachedToken? _token;

    public async ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    public ValueTask<bool> OnUnauthorizedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Drop the token the server rejected, unless another request already replaced it.
        var rejected = response.RequestMessage?.Headers.Authorization?.Parameter;
        var current = _token;
        if (current != null && current.Value == rejected)
        {
            Interlocked.CompareExchange(ref _token, null, current);
        }
        return ValueTask.FromResult(true);
    }

    private async ValueTask<CachedToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = _token;
        if (IsUsable(token))
        {
            return token!;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = _token;
            if (IsUsable(token))
            {
                return token!;
            }
            token = await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
            _token = token;
            return token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsUsable(CachedToken? token) =>
        token != null && (token.ExpiresAt == null || options.TimeProvider.GetUtcNow() < token.ExpiresAt - options.RefreshBeforeExpiry);

    private async Task<CachedToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        if (!string.IsNullOrEmpty(options.Scope))
        {
            form.Add(new("scope", options.Scope));
        }
        if (options.AdditionalParameters != null)
        {
            form.AddRange(options.AdditionalParameters);
        }

        var tokens = await OAuth2.RequestTokensAsync(options.TokenClient, options.TokenUrl, options.ClientId, options.ClientSecret,
            options.SendCredentialsInBody, form, options.TimeProvider, cancellationToken).ConfigureAwait(false);
        return new CachedToken(tokens.AccessToken, tokens.ExpiresAt);
    }

    private sealed record CachedToken(string Value, DateTimeOffset? ExpiresAt);
}
