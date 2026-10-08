using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nexar;

internal static class UrlBuilder
{
    public static bool TryParseHttpUrl(string value, [NotNullWhen(true)] out Uri? uri)
    {
        // Uri treats "/path" as an absolute file:// URI on Unix, so the scheme has to be checked.
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static Uri Build(Uri? baseUrl, string url, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        string resolved;
        if (TryParseHttpUrl(url, out var absolute))
        {
            resolved = absolute.AbsoluteUri;
        }
        else if (baseUrl != null)
        {
            resolved = url.Length == 0
                ? baseUrl.AbsoluteUri
                : baseUrl.AbsoluteUri.TrimEnd('/') + "/" + url.TrimStart('/');
        }
        else
        {
            throw new NexarException(
                ErrorKind.Builder,
                $"'{url}' is not an absolute http(s) URL and the client has no base URL.");
        }

        // The query goes before the fragment: /page?b=2#section
        var fragment = string.Empty;
        var hash = resolved.IndexOf('#');
        if (hash >= 0)
        {
            fragment = resolved[hash..];
            resolved = resolved[..hash];
        }

        if (query.Count > 0)
        {
            var builder = new StringBuilder(resolved);
            var separator = resolved.Contains('?') ? '&' : '?';
            foreach (var (key, value) in query)
            {
                builder.Append(separator)
                    .Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value));
                separator = '&';
            }
            resolved = builder.ToString();
        }
        resolved += fragment;

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var result))
        {
            throw new NexarException(ErrorKind.Builder, $"'{resolved}' is not a valid URL.");
        }
        return result;
    }
}
