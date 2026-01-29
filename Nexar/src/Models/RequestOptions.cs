namespace Nexar.Models;

/// <summary>
/// Content type options for request body.
/// </summary>
public enum ContentType
{
    /// <summary>
    /// JSON content type (application/json).
    /// </summary>
    Json,

    /// <summary>
    /// Form URL encoded content type (application/x-www-form-urlencoded).
    /// </summary>
    FormUrlEncoded,

    /// <summary>
    /// Multipart form data content type (multipart/form-data).
    /// </summary>
    FormData,

    /// <summary>
    /// Binary content type (application/octet-stream).
    /// </summary>
    Binary
}

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
    /// Request body data.
    /// For Json: any object that can be serialized to JSON.
    /// For FormUrlEncoded and FormData: Dictionary&lt;string, string&gt; or Dictionary&lt;string, object&gt;.
    /// For Binary: byte[] or Stream.
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Content type for the request body. Defaults to Json.
    /// </summary>
    public ContentType ContentType { get; set; } = ContentType.Json;

    /// <summary>
    /// Timeout in milliseconds.
    /// </summary>
    public int? Timeout { get; set; }

    /// <summary>
    /// Base URL to be prepended to url.
    /// </summary>
    public string? BaseURL { get; set; }

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Validate SSL certificates.
    /// </summary>
    public bool? ValidateSsl { get; set; }
}
