using Nexar.Helpers;
using Nexar.Models;

namespace Nexar;

/// <summary>
/// Fluent API builder for creating HTTP requests.
/// </summary>
public class NexarRequestBuilder
{
    private readonly Nexar _nexar;
    private string _url = string.Empty;
    private readonly Dictionary<string, string> _headers = new();
    private object? _body;
    private ContentType _contentType = ContentType.Json;
    private readonly QueryStringBuilder _queryBuilder = new();

    internal NexarRequestBuilder(Nexar nexar)
    {
        _nexar = nexar;
    }

    /// <summary>
    /// Sets the URL for the request.
    /// </summary>
    public NexarRequestBuilder Url(string url)
    {
        _url = url;
        return this;
    }

    /// <summary>
    /// Adds a header to the request.
    /// </summary>
    public NexarRequestBuilder WithHeader(string key, string value)
    {
        _headers[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple headers to the request.
    /// </summary>
    public NexarRequestBuilder WithHeaders(Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            _headers[header.Key] = header.Value;
        }
        return this;
    }

    /// <summary>
    /// Sets the request body.
    /// </summary>
    public NexarRequestBuilder WithBody<T>(T body)
    {
        _body = body;
        return this;
    }

    /// <summary>
    /// Sets the content type for the request body.
    /// </summary>
    public NexarRequestBuilder WithContentType(ContentType contentType)
    {
        _contentType = contentType;
        return this;
    }

    /// <summary>
    /// Adds a query parameter to the request.
    /// </summary>
    public NexarRequestBuilder WithQuery(string key, string value)
    {
        _queryBuilder.Add(key, value);
        return this;
    }

    /// <summary>
    /// Adds a query parameter to the request.
    /// </summary>
    public NexarRequestBuilder WithQuery(string key, object value)
    {
        _queryBuilder.Add(key, value);
        return this;
    }

    /// <summary>
    /// Adds multiple query parameters to the request.
    /// </summary>
    public NexarRequestBuilder WithQueries(Dictionary<string, string> queries)
    {
        _queryBuilder.AddRange(queries);
        return this;
    }

    /// <summary>
    /// Adds Bearer token authentication.
    /// </summary>
    public NexarRequestBuilder WithBearerToken(string token)
    {
        _headers["Authorization"] = Auth.AuthHelper.Bearer(token);
        return this;
    }

    /// <summary>
    /// Adds Basic authentication.
    /// </summary>
    public NexarRequestBuilder WithBasicAuth(string username, string password)
    {
        _headers["Authorization"] = Auth.AuthHelper.Basic(username, password);
        return this;
    }

    /// <summary>
    /// Adds API Key authentication.
    /// </summary>
    public NexarRequestBuilder WithApiKey(string headerName, string apiKey)
    {
        _headers[headerName] = apiKey;
        return this;
    }

    private string BuildUrl()
    {
        if (string.IsNullOrEmpty(_url))
            throw new InvalidOperationException("URL must be set before sending a request. Use .Url() method.");
        return _url + _queryBuilder.Build();
    }

    /// <summary>
    /// Sends a GET request.
    /// </summary>
    public async Task<NexarResponse<T>> GetAsync<T>()
    {
        return await _nexar.GetAsync<T>(BuildUrl(), _headers);
    }

    /// <summary>
    /// Sends a POST request using the body set via WithBody().
    /// </summary>
    public async Task<NexarResponse<T>> PostAsync<T>()
    {
        return await _nexar.PostAsync<T>(BuildUrl(), _body, _headers, _contentType);
    }

    /// <summary>
    /// Sends a POST request with the specified body.
    /// </summary>
    public async Task<NexarResponse<T>> PostAsync<T>(object body)
    {
        return await _nexar.PostAsync<T>(BuildUrl(), body, _headers, _contentType);
    }

    /// <summary>
    /// Sends a PUT request using the body set via WithBody().
    /// </summary>
    public async Task<NexarResponse<T>> PutAsync<T>()
    {
        return await _nexar.PutAsync<T>(BuildUrl(), _body, _headers, _contentType);
    }

    /// <summary>
    /// Sends a PUT request with the specified body.
    /// </summary>
    public async Task<NexarResponse<T>> PutAsync<T>(object body)
    {
        return await _nexar.PutAsync<T>(BuildUrl(), body, _headers, _contentType);
    }

    /// <summary>
    /// Sends a DELETE request.
    /// </summary>
    public async Task<NexarResponse<T>> DeleteAsync<T>()
    {
        return await _nexar.DeleteAsync<T>(BuildUrl(), _headers);
    }

    /// <summary>
    /// Sends a PATCH request using the body set via WithBody().
    /// </summary>
    public async Task<NexarResponse<T>> PatchAsync<T>()
    {
        return await _nexar.PatchAsync<T>(BuildUrl(), _body, _headers, _contentType);
    }

    /// <summary>
    /// Sends a PATCH request with the specified body.
    /// </summary>
    public async Task<NexarResponse<T>> PatchAsync<T>(object body)
    {
        return await _nexar.PatchAsync<T>(BuildUrl(), body, _headers, _contentType);
    }
}
