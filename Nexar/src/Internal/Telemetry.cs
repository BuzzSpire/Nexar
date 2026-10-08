using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Nexar;

/// <summary>
/// The <see cref="ActivitySource"/> and <see cref="Meter"/> named <c>Nexar</c>.
/// Instrument and attribute names follow the OpenTelemetry HTTP client semantic conventions.
/// </summary>
internal static class Telemetry
{
    public const string Name = "Nexar";

    private static readonly string? Version = typeof(Telemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public static readonly ActivitySource ActivitySource = new(Name, Version);

    private static readonly Meter Meter = new(Name, Version);

    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "http.client.request.duration", unit: "s", description: "Duration of HTTP client requests, including retries.");

    public static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        "http.client.active_requests", unit: "{request}", description: "Number of outstanding HTTP client requests.");

    public static readonly Counter<long> Resends = Meter.CreateCounter<long>(
        "nexar.client.resends", unit: "{request}", description: "Number of times a request was sent again (retries and re-authentication).");
}

/// <summary>
/// Hides secrets in URLs and headers before they reach logs, spans or metrics.
/// </summary>
internal sealed class Redactor
{
    public const string Mask = "REDACTED";

    public static readonly string[] DefaultHeaders =
        ["Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "Api-Key"];

    public static readonly string[] DefaultQueryParameters =
        ["api_key", "apikey", "key", "access_token", "refresh_token", "id_token", "token", "client_secret", "password", "signature", "sig"];

    private readonly HashSet<string> _headers;
    private readonly HashSet<string> _queryParameters;

    public Redactor(IEnumerable<string> headers, IEnumerable<string> queryParameters)
    {
        _headers = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        _queryParameters = new HashSet<string>(queryParameters, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSensitiveHeader(string name, IAuthenticator? authenticator) =>
        _headers.Contains(name) || (authenticator is IRedactionHints hints && hints.Headers.Contains(name, StringComparer.OrdinalIgnoreCase));

    public string RedactHeader(string name, IEnumerable<string> values, IAuthenticator? authenticator) =>
        IsSensitiveHeader(name, authenticator) ? Mask : string.Join(", ", values);

    /// <summary>
    /// The URL without user info, with sensitive query values replaced by <see cref="Mask"/>.
    /// </summary>
    public string RedactUrl(Uri url, IAuthenticator? authenticator)
    {
        var builder = new UriBuilder(url) { UserName = "", Password = "", Fragment = "" };
        var query = url.Query.TrimStart('?');
        if (query.Length > 0)
        {
            var hints = authenticator as IRedactionHints;
            builder.Query = string.Join('&', query.Split('&').Select(pair =>
            {
                var separator = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
                var sensitive = _queryParameters.Contains(key)
                    || (hints != null && hints.QueryParameters.Contains(key, StringComparer.OrdinalIgnoreCase));
                return sensitive && separator >= 0 ? $"{pair[..separator]}={Mask}" : pair;
            }));
        }
        return builder.Uri.AbsoluteUri;
    }
}

/// <summary>
/// Lets an authenticator tell the <see cref="Redactor"/> which header or query names carry its secret.
/// </summary>
internal interface IRedactionHints
{
    IReadOnlyCollection<string> Headers { get; }

    IReadOnlyCollection<string> QueryParameters { get; }
}
