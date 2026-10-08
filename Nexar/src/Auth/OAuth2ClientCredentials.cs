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
    private static readonly Lazy<NexarClient> SharedTokenClient = new(() => new NexarClient());

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

        var request = (options.TokenClient ?? SharedTokenClient.Value)
            .Post(options.TokenUrl)
            .NoAuth()
            .Header("Accept", "application/json");

        if (options.SendCredentialsInBody)
        {
            form.Add(new("client_id", options.ClientId));
            form.Add(new("client_secret", options.ClientSecret));
        }
        else
        {
            // RFC 6749 §2.3.1: form-encode the credentials before Basic encoding.
            request.BasicAuth(WebUtility.UrlEncode(options.ClientId), WebUtility.UrlEncode(options.ClientSecret));
        }

        string body;
        HttpStatusCode status;
        try
        {
            using var response = await request.Form(form).Send(cancellationToken).ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Text(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new NexarException(
                    ErrorKind.Auth,
                    $"Token request to {options.TokenUrl} failed with HTTP {(int)status}{DescribeError(body)}.",
                    response.Url,
                    status);
            }
        }
        catch (NexarException ex) when (ex.Kind != ErrorKind.Auth)
        {
            throw new NexarException(ErrorKind.Auth, $"Token request to {options.TokenUrl} failed: {ex.Message}", ex.Url, innerException: ex);
        }

        return ParseToken(body);
    }

    private CachedToken ParseToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new NexarException(ErrorKind.Auth, $"Token response from {options.TokenUrl} has no access_token.");
            }

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var expiresIn))
            {
                var seconds = expiresIn.ValueKind == JsonValueKind.String
                    ? double.Parse(expiresIn.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : expiresIn.GetDouble();
                expiresAt = options.TimeProvider.GetUtcNow().AddSeconds(seconds);
            }

            return new CachedToken(accessToken, expiresAt);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new NexarException(ErrorKind.Auth, $"Token response from {options.TokenUrl} is not valid: {ex.Message}", innerException: ex);
        }
    }

    private static string DescribeError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                var description = root.TryGetProperty("error_description", out var d) ? $" ({d.GetString()})" : "";
                return $": {error.GetString()}{description}";
            }
        }
        catch (JsonException)
        {
        }
        return "";
    }

    private sealed record CachedToken(string Value, DateTimeOffset? ExpiresAt);
}
