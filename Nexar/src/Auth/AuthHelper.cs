using System.Text;

namespace Nexar.Auth;

/// <summary>
/// Helper class for authentication.
/// </summary>
public static class AuthHelper
{
    /// <summary>
    /// Creates a Bearer token authentication header value.
    /// </summary>
    public static string Bearer(string token)
    {
        return $"Bearer {token}";
    }

    /// <summary>
    /// Creates a Basic authentication header value.
    /// </summary>
    public static string Basic(string username, string password)
    {
        var credentials = $"{username}:{password}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        return $"Basic {encodedCredentials}";
    }

    /// <summary>
    /// Creates an API Key authentication header.
    /// </summary>
    public static KeyValuePair<string, string> ApiKey(string headerName, string apiKey)
    {
        return new KeyValuePair<string, string>(headerName, apiKey);
    }
}
