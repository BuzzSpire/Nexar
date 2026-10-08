using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexar;

internal static partial class UrlBuilder
{
    [GeneratedRegex(@"\{([A-Za-z0-9_.\-]+)\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Replaces <c>{name}</c> placeholders with escaped values. Every placeholder needs a value and every value a placeholder.
    /// </summary>
    public static string ExpandPath(string template, IReadOnlyDictionary<string, string> parameters)
    {
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        var expanded = Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!parameters.TryGetValue(name, out var value))
            {
                missing.Add(name);
                return match.Value;
            }
            used.Add(name);
            return Uri.EscapeDataString(value);
        });

        if (missing.Count > 0)
        {
            throw new NexarException(ErrorKind.Builder, $"No value for path parameter(s) {string.Join(", ", missing.Select(m => $"{{{m}}}"))} in '{template}'.");
        }

        var unused = parameters.Keys.Where(k => !used.Contains(k)).ToList();
        if (unused.Count > 0)
        {
            throw new NexarException(ErrorKind.Builder, $"Path parameter(s) {string.Join(", ", unused)} have no placeholder in '{template}'.");
        }

        return expanded;
    }

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
