using System.Net.Http.Headers;

namespace Nexar;

/// <summary>
/// Configuration for <see cref="Auth.OAuth2RefreshToken"/>.
/// </summary>
public sealed class OAuth2RefreshTokenOptions
{
    /// <summary>The absolute URL of the token endpoint.</summary>
    public required string TokenUrl { get; init; }

    /// <summary>The client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>The refresh token, e.g. from <see cref="OAuth2.ExchangeCodeAsync"/> or the app's secure storage.</summary>
    public required string RefreshToken { get; init; }

    /// <summary>The secret of a confidential client; null for public (desktop, mobile, CLI) clients.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>The scope to request on refresh, if the server needs it.</summary>
    public string? Scope { get; init; }

    /// <summary>Tokens already at hand (e.g. from the code exchange), so the first request needs no refresh.</summary>
    public OAuth2Tokens? InitialTokens { get; init; }

    /// <summary>
    /// Called after every refresh with the new tokens, so the app can persist a rotated refresh token.
    /// </summary>
    public Func<OAuth2Tokens, CancellationToken, ValueTask>? OnTokensRefreshed { get; init; }

    /// <summary>How long before expiry an access token is replaced. Defaults to 30 seconds.</summary>
    public TimeSpan RefreshBeforeExpiry { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The client used to call the token endpoint. Defaults to a shared client with default settings.</summary>
    public NexarClient? TokenClient { get; init; }

    /// <summary>The clock used for token expiry. Useful in tests.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// Keeps an access token fresh with the refresh token grant, rotating the refresh token when the server issues a new one.
/// </summary>
internal sealed class OAuth2RefreshTokenAuthenticator : IAuthenticator
{
    private readonly OAuth2RefreshTokenOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile OAuth2Tokens? _tokens;
    private string _refreshToken;

    public OAuth2RefreshTokenAuthenticator(OAuth2RefreshTokenOptions options)
    {
        _options = options;
        _tokens = options.InitialTokens;
        _refreshToken = options.InitialTokens?.RefreshToken ?? options.RefreshToken;
    }

    public async ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokens = await GetTokensAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    public ValueTask<bool> OnUnauthorizedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Drop the rejected access token unless another request already replaced it; the re-send then refreshes.
        var rejected = response.RequestMessage?.Headers.Authorization?.Parameter;
        var current = _tokens;
        if (current != null && current.AccessToken == rejected)
        {
            Interlocked.CompareExchange(ref _tokens, null, current);
        }
        return ValueTask.FromResult(true);
    }

    private bool IsUsable(OAuth2Tokens? tokens) =>
        tokens != null && (tokens.ExpiresAt == null || _options.TimeProvider.GetUtcNow() < tokens.ExpiresAt - _options.RefreshBeforeExpiry);

    private async ValueTask<OAuth2Tokens> GetTokensAsync(CancellationToken cancellationToken)
    {
        var tokens = _tokens;
        if (IsUsable(tokens))
        {
            return tokens!;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            tokens = _tokens;
            if (IsUsable(tokens))
            {
                return tokens!;
            }

            var form = new List<KeyValuePair<string, string>> { new("grant_type", "refresh_token"), new("refresh_token", _refreshToken) };
            if (!string.IsNullOrEmpty(_options.Scope))
            {
                form.Add(new("scope", _options.Scope));
            }
            tokens = await OAuth2.RequestTokensAsync(_options.TokenClient, _options.TokenUrl, _options.ClientId, _options.ClientSecret,
                credentialsInBody: false, form, _options.TimeProvider, cancellationToken).ConfigureAwait(false);

            // Servers that rotate refresh tokens return a new one; others keep the old one valid.
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                _refreshToken = tokens.RefreshToken;
            }
            else
            {
                tokens = tokens with { RefreshToken = _refreshToken };
            }
            _tokens = tokens;

            if (_options.OnTokensRefreshed is { } onRefreshed)
            {
                await onRefreshed(tokens, cancellationToken).ConfigureAwait(false);
            }
            return tokens;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
