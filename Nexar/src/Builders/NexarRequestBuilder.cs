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

    /// <summary>
    /// Sends a GET request.
    /// </summary>
    public async Task<NexarResponse<T>> GetAsync<T>()
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.GetAsync<T>(fullUrl, _headers);
    }

    /// <summary>
    /// Sends a POST request.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PostAsync<TResponse>()
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PostAsync<object, TResponse>(fullUrl, _headers, _body, _contentType);
    }

    /// <summary>
    /// Sends a POST request with a typed body.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PostAsync<TRequest, TResponse>(TRequest body)
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PostAsync<TRequest, TResponse>(fullUrl, _headers, body, _contentType);
    }

    /// <summary>
    /// Sends a PUT request.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PutAsync<TResponse>()
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PutAsync<object, TResponse>(fullUrl, _headers, _body, _contentType);
    }

    /// <summary>
    /// Sends a PUT request with a typed body.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PutAsync<TRequest, TResponse>(TRequest body)
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PutAsync<TRequest, TResponse>(fullUrl, _headers, body, _contentType);
    }

    /// <summary>
    /// Sends a DELETE request.
    /// </summary>
    public async Task<NexarResponse<T>> DeleteAsync<T>()
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.DeleteAsync<T>(fullUrl, _headers);
    }

    /// <summary>
    /// Sends a PATCH request.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PatchAsync<TResponse>()
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PatchAsync<object, TResponse>(fullUrl, _headers, _body, _contentType);
    }

    /// <summary>
    /// Sends a PATCH request with a typed body.
    /// </summary>
    public async Task<NexarResponse<TResponse>> PatchAsync<TRequest, TResponse>(TRequest body)
    {
        var fullUrl = _url + _queryBuilder.Build();
        return await _nexar.PatchAsync<TRequest, TResponse>(fullUrl, _headers, body, _contentType);
    }
}
