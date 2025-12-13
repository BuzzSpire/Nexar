namespace Nexar.Models;

/// <summary>
/// Request configuration options.
/// </summary>
public class RequestOptions
{
    /// <summary>
    /// Request URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH, HEAD).
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Request headers.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// URL parameters (query string).
    /// </summary>
    public Dictionary<string, string>? Params { get; set; }

    /// <summary>
    /// Request body data.
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Timeout in milliseconds.
    /// </summary>
    public int? Timeout { get; set; }

    /// <summary>
    /// Base URL to be prepended to url.
    /// </summary>
    public string? BaseURL { get; set; }

    /// <summary>
    /// Response type (json, text, blob, etc.).
    /// </summary>
    public string ResponseType { get; set; } = "json";

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Validate SSL certificates.
    /// </summary>
    public bool? ValidateSsl { get; set; }
}
