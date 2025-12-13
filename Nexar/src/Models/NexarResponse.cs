using System.Net;

namespace Nexar.Models;

/// <summary>
/// Represents an HTTP response from Nexar.
/// </summary>
/// <typeparam name="T">The type of data in the response.</typeparam>
public class NexarResponse<T>
{
    /// <summary>
    /// The response data deserialized from JSON.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// HTTP status code as integer.
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// HTTP status code as enum.
    /// </summary>
    public HttpStatusCode StatusCode { get; set; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// Response headers.
    /// </summary>
    public Dictionary<string, IEnumerable<string>> Headers { get; set; } = new();

    /// <summary>
    /// Request configuration that was used for the request.
    /// </summary>
    public RequestOptions? Config { get; set; }

    /// <summary>
    /// Raw response content as string.
    /// </summary>
    public string RawContent { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if the request was successful (2xx status code).
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if request failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Exception if request failed.
    /// </summary>
    public Exception? Exception { get; set; }
}
