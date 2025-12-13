using System.Web;

namespace Nexar.Helpers;

/// <summary>
/// Helper class for building query strings.
/// </summary>
public class QueryStringBuilder
{
    private readonly Dictionary<string, string> _parameters = new();

    /// <summary>
    /// Adds a parameter to the query string.
    /// </summary>
    public QueryStringBuilder Add(string key, string value)
    {
        _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Adds a parameter to the query string.
    /// </summary>
    public QueryStringBuilder Add(string key, object value)
    {
        _parameters[key] = value?.ToString() ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Adds multiple parameters from a dictionary.
    /// </summary>
    public QueryStringBuilder AddRange(Dictionary<string, string> parameters)
    {
        foreach (var param in parameters)
        {
            _parameters[param.Key] = param.Value;
        }
        return this;
    }

    /// <summary>
    /// Builds the query string.
    /// </summary>
    public string Build()
    {
        if (_parameters.Count == 0)
            return string.Empty;

        var encodedParams = _parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");

        return "?" + string.Join("&", encodedParams);
    }

    /// <summary>
    /// Creates a new instance of QueryStringBuilder.
    /// </summary>
    public static QueryStringBuilder Create() => new();
}
