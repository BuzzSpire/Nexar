using Nexar.Models;

namespace Nexar.Http.Abstract;

/// <summary>
/// INexar interface defines the contract for HTTP operations.
/// </summary>
public interface INexar
{
    /// <summary>
    /// Sends a GET request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="headers">The headers to be added to the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> GetAsync(string url, Dictionary<string, string> headers);

    /// <summary>
    /// Sends a GET request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> GetAsync<T>(string url, Dictionary<string, string>? headers = null);

    /// <summary>
    /// Sends a POST request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="headers">The headers to be added to the request.</param>
    /// <param name="body">The body of the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> PostAsync<T>(string url, Dictionary<string, string> headers, T body);

    /// <summary>
    /// Sends a POST request and returns a typed response.
    /// </summary>
    Task<NexarResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default);

    /// <summary>
    /// Sends a PUT request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="headers">The headers to be added to the request.</param>
    /// <param name="body">The body of the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> PutAsync<T>(string url, Dictionary<string, string> headers, T body);

    /// <summary>
    /// Sends a PUT request and returns a typed response.
    /// </summary>
    Task<NexarResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default);

    /// <summary>
    /// Sends a DELETE request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="headers">The headers to be added to the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> DeleteAsync(string url, Dictionary<string, string> headers);

    /// <summary>
    /// Sends a DELETE request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> DeleteAsync<T>(string url, Dictionary<string, string>? headers = null);

    /// <summary>
    /// Sends a PATCH request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="headers">The headers to be added to the request.</param>
    /// <param name="body">The body of the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> PatchAsync<T>(string url, Dictionary<string, string> headers, T body);

    /// <summary>
    /// Sends a PATCH request and returns a typed response.
    /// </summary>
    Task<NexarResponse<TResponse>> PatchAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default);

    /// <summary>
    /// Sends a HEAD request to the specified Uri as an asynchronous operation.
    /// </summary>
    /// <param name="url">The Url the request is sent to.</param>
    /// <param name="header">The headers to be added to the request.</param>
    /// <returns>The HTTP response message as a string.</returns>
    Task<string> HeadAsync(string url, Dictionary<string, string> headers);

    /// <summary>
    /// Sends a HEAD request and returns a typed response.
    /// </summary>
    Task<NexarResponse<T>> HeadAsync<T>(string url, Dictionary<string, string>? headers = null);
}
