using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexar;

/// <summary>
/// Tokens returned by an OAuth 2.0 token endpoint.
/// </summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="TokenType">The token type, usually <c>Bearer</c>.</param>
/// <param name="ExpiresAt">When the access token expires, if the server said.</param>
/// <param name="RefreshToken">The refresh token, if one was issued (possibly a new, rotated one).</param>
/// <param name="Scope">The granted scope, if the server reported it.</param>
/// <param name="IdToken">The OpenID Connect ID token, if present.</param>
public sealed record OAuth2Tokens(
    string AccessToken,
    string TokenType,
    DateTimeOffset? ExpiresAt,
    string? RefreshToken,
    string? Scope,
    string? IdToken);

/// <summary>
/// A PKCE (RFC 7636) verifier and challenge for the authorization code flow.
/// Send <see cref="Challenge"/> and <see cref="Method"/> in the authorization request, and keep <see cref="Verifier"/>
/// for <see cref="OAuth2.ExchangeCodeAsync"/>.
/// </summary>
public sealed record PkceChallenge(string Verifier, string Challenge, string Method);

/// <summary>
/// OAuth 2.0 token endpoint helpers: PKCE, the authorization code exchange and the refresh token grant.
/// For authenticators that keep tokens fresh, see <see cref="Auth.OAuth2RefreshToken"/> and
/// <see cref="Auth.OAuth2ClientCredentials(string, string, string, string?)"/>.
/// </summary>
public static class OAuth2
{
    private static readonly Lazy<NexarClient> SharedTokenClient = new(() => new NexarClient());

    /// <summary>Creates a PKCE verifier and its S256 challenge.</summary>
    public static PkceChallenge CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new PkceChallenge(verifier, S256Challenge(verifier), "S256");
    }

    internal static string S256Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>
    /// Exchanges an authorization code (after the browser redirect) for tokens (RFC 6749 §4.1.3 with PKCE).
    /// </summary>
    /// <param name="client">The client that calls the token endpoint; null uses a shared default client.</param>
    /// <param name="tokenUrl">The absolute token endpoint URL.</param>
    /// <param name="clientId">The client identifier.</param>
    /// <param name="code">The code from the redirect.</param>
    /// <param name="codeVerifier">The PKCE verifier from <see cref="CreatePkce"/>, or null without PKCE.</param>
    /// <param name="redirectUri">The redirect URI used in the authorization request.</param>
    /// <param name="clientSecret">The secret of a confidential client, sent with Basic authentication; null for public clients.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="NexarException">The token endpoint failed or rejected the code (<see cref="ErrorKind.Auth"/>).</exception>
    public static Task<OAuth2Tokens> ExchangeCodeAsync(NexarClient? client, string tokenUrl, string clientId, string code,
        string? codeVerifier, string redirectUri, string? clientSecret = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri)
        };
        if (!string.IsNullOrEmpty(codeVerifier))
        {
            form.Add(new("code_verifier", codeVerifier));
        }
        return RequestTokensAsync(client, tokenUrl, clientId, clientSecret, credentialsInBody: false, form, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Gets new tokens with a refresh token (RFC 6749 §6). The result may carry a new, rotated refresh token.
    /// </summary>
    /// <exception cref="NexarException">The token endpoint failed or rejected the refresh token, e.g. <c>invalid_grant</c> (<see cref="ErrorKind.Auth"/>).</exception>
    public static Task<OAuth2Tokens> RefreshAsync(NexarClient? client, string tokenUrl, string clientId, string refreshToken,
        string? clientSecret = null, string? scope = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "refresh_token"), new("refresh_token", refreshToken) };
        if (!string.IsNullOrEmpty(scope))
        {
            form.Add(new("scope", scope));
        }
        return RequestTokensAsync(client, tokenUrl, clientId, clientSecret, credentialsInBody: false, form, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Posts <paramref name="form"/> to the token endpoint and parses the response. Confidential clients authenticate
    /// with Basic (RFC 6749 §2.3.1) or in the body; public clients send only <c>client_id</c>.
    /// </summary>
    internal static async Task<OAuth2Tokens> RequestTokensAsync(NexarClient? client, string tokenUrl, string clientId, string? clientSecret,
        bool credentialsInBody, List<KeyValuePair<string, string>> form, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenUrl);
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        var request = (client ?? SharedTokenClient.Value)
            .Post(tokenUrl)
            .NoAuth()
            .Header("Accept", "application/json");

        if (clientSecret == null || credentialsInBody)
        {
            form.Add(new("client_id", clientId));
            if (clientSecret != null)
            {
                form.Add(new("client_secret", clientSecret));
            }
        }
        else
        {
            // RFC 6749 §2.3.1: form-encode the credentials before Basic encoding.
            request.BasicAuth(WebUtility.UrlEncode(clientId), WebUtility.UrlEncode(clientSecret));
        }

        string body;
        try
        {
            using var response = await request.Form(form).Send(cancellationToken).ConfigureAwait(false);
            body = await response.Text(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new NexarException(
                    ErrorKind.Auth,
                    $"Token request to {tokenUrl} failed with HTTP {response.Status}{DescribeError(body)}.",
                    response.Url,
                    response.StatusCode);
            }
        }
        catch (NexarException ex) when (ex.Kind != ErrorKind.Auth)
        {
            throw new NexarException(ErrorKind.Auth, $"Token request to {tokenUrl} failed: {ex.Message}", ex.Url, innerException: ex);
        }

        return Parse(body, tokenUrl, clock);
    }

    private static OAuth2Tokens Parse(string body, string tokenUrl, TimeProvider clock)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var accessToken = Text("access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new NexarException(ErrorKind.Auth, $"Token response from {tokenUrl} has no access_token.");
            }

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var expiresIn))
            {
                var seconds = expiresIn.ValueKind == JsonValueKind.String
                    ? double.Parse(expiresIn.GetString()!, CultureInfo.InvariantCulture)
                    : expiresIn.GetDouble();
                expiresAt = clock.GetUtcNow().AddSeconds(seconds);
            }

            return new OAuth2Tokens(accessToken, Text("token_type") ?? "Bearer", expiresAt, Text("refresh_token"), Text("scope"), Text("id_token"));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new NexarException(ErrorKind.Auth, $"Token response from {tokenUrl} is not valid: {ex.Message}", innerException: ex);
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

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
