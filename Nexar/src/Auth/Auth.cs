using System.Net.Http.Headers;
using System.Text;

namespace Nexar;

/// <summary>
/// Ready-made <see cref="IAuthenticator"/> implementations.
/// </summary>
/// <example>
/// <code>
/// var client = NexarClient.Builder()
///     .BaseUrl("https://api.example.com")
///     .Auth(Auth.OAuth2ClientCredentials("https://login.example.com/token", clientId, clientSecret))
///     .Build();
///
/// await client.Get("/legacy").Auth(Auth.Digest("user", "pass")).Send();
/// </code>
/// </example>
public static class Auth
{
    /// <summary>
    /// <c>Authorization: Bearer {token}</c> with a fixed token.
    /// </summary>
    public static IAuthenticator Bearer(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        HeaderValidator.Validate("Authorization", token);
        return new DelegateAuthenticator((request, _) =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// <c>Authorization: Bearer {token}</c> with a token fetched before every request.
    /// The provider is responsible for caching.
    /// </summary>
    public static IAuthenticator Bearer(Func<CancellationToken, ValueTask<string>> tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        return new DelegateAuthenticator(async (request, cancellationToken) =>
        {
            var token = await tokenProvider(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("The bearer token provider returned an empty token.");
            }
            HeaderValidator.Validate("Authorization", token);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });
    }

    /// <summary>
    /// HTTP Basic authentication (RFC 7617).
    /// </summary>
    public static IAuthenticator Basic(string username, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(username);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new DelegateAuthenticator((request, _) =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// An API key sent in a header, e.g. <c>X-Api-Key: {key}</c>.
    /// </summary>
    public static IAuthenticator ApiKeyHeader(string headerName, string apiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(headerName);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        HeaderValidator.Validate(headerName, apiKey);
        return new DelegateAuthenticator((request, _) =>
        {
            request.Headers.Remove(headerName);
            request.Headers.TryAddWithoutValidation(headerName, apiKey);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// An API key sent as a query parameter, e.g. <c>?api_key={key}</c>.
    /// </summary>
    public static IAuthenticator ApiKeyQuery(string parameterName, string apiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        var pair = $"{Uri.EscapeDataString(parameterName)}={Uri.EscapeDataString(apiKey)}";
        return new DelegateAuthenticator((request, _) =>
        {
            var builder = new UriBuilder(request.RequestUri!);
            var query = builder.Query.TrimStart('?');
            builder.Query = query.Length == 0 ? pair : $"{query}&{pair}";
            request.RequestUri = builder.Uri;
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// HTTP Digest authentication (RFC 7616): MD5, SHA-256 and their <c>-sess</c> variants with <c>qop=auth</c>.
    /// </summary>
    /// <remarks>
    /// The first request is sent without credentials. After the server's <c>401</c> challenge, the request is
    /// re-sent once, and later requests authenticate up front with the same nonce.
    /// Requests with a <see cref="Stream"/> body cannot be re-sent and will get the <c>401</c>.
    /// </remarks>
    public static IAuthenticator Digest(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        return new DigestAuthenticator(username, password);
    }

    /// <summary>
    /// OAuth 2.0 client credentials grant (RFC 6749 §4.4). Tokens are cached until shortly before they
    /// expire and refreshed after a <c>401</c>.
    /// </summary>
    public static IAuthenticator OAuth2ClientCredentials(string tokenUrl, string clientId, string clientSecret, string? scope = null)
    {
        return OAuth2ClientCredentials(new OAuth2ClientCredentialsOptions
        {
            TokenUrl = tokenUrl,
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scope = scope
        });
    }

    /// <summary>
    /// OAuth 2.0 client credentials grant with full configuration.
    /// </summary>
    public static IAuthenticator OAuth2ClientCredentials(OAuth2ClientCredentialsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TokenUrl);
        ArgumentException.ThrowIfNullOrEmpty(options.ClientId);
        ArgumentNullException.ThrowIfNull(options.ClientSecret);
        return new OAuth2ClientCredentialsAuthenticator(options);
    }

    /// <summary>
    /// Any other scheme: request signing (HMAC, AWS SigV4), custom headers, ...
    /// The request body, if any, can be read inside <paramref name="apply"/>.
    /// </summary>
    public static IAuthenticator Custom(Func<HttpRequestMessage, CancellationToken, ValueTask> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new DelegateAuthenticator(apply);
    }

    private sealed class DelegateAuthenticator(Func<HttpRequestMessage, CancellationToken, ValueTask> apply) : IAuthenticator
    {
        public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => apply(request, cancellationToken);
    }
}
