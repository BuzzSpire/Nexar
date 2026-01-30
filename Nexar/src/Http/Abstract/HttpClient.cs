using Nexar.Models;

namespace Nexar.Http.Abstract;

/// <summary>
/// INexar interface defines the contract for HTTP operations.
/// </summary>
public interface INexar
{
    /// <summary>
    /// Sends a GET request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> GetAsync<T>(string url, Dictionary<string, string>? headers = null);

    /// <summary>
    /// Sends a POST request with an optional body and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> PostAsync<T>(
        string url,
        object? body = null,
        Dictionary<string, string>? headers = null,
        ContentType contentType = ContentType.Json);

    /// <summary>
    /// Sends a PUT request with an optional body and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> PutAsync<T>(
        string url,
        object? body = null,
        Dictionary<string, string>? headers = null,
        ContentType contentType = ContentType.Json);

    /// <summary>
    /// Sends a DELETE request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> DeleteAsync<T>(string url, Dictionary<string, string>? headers = null);

    /// <summary>
    /// Sends a PATCH request with an optional body and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> PatchAsync<T>(
        string url,
        object? body = null,
        Dictionary<string, string>? headers = null,
        ContentType contentType = ContentType.Json);

    /// <summary>
    /// Sends a HEAD request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> HeadAsync<T>(string url, Dictionary<string, string>? headers = null);
}
