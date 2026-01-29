using System.Net;
using System.Text;
using System.Text.Json;
using Nexar.Configuration;
using Nexar.Http.Abstract;
using Nexar.Interceptors;
using Nexar.Models;

namespace Nexar;

public class Nexar : INexar, IDisposable
{
    private readonly HttpClient _client;
    private readonly NexarConfig _config;
    private readonly InterceptorCollection _interceptors = new();

    // Static default instance for static method usage
    private static Nexar? _defaultInstance;
    private static Nexar DefaultInstance => _defaultInstance ??= new Nexar();

    public Nexar() : this(new NexarConfig())
    {
    }

    public Nexar(NexarConfig config)
    {
        _config = config;

        var handler = new HttpClientHandler();
        if (!_config.ValidateSslCertificates)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };

        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            _client.BaseAddress = new Uri(_config.BaseUrl);
        }

        foreach (var header in _config.DefaultHeaders)
        {
            _client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    public InterceptorCollection Interceptors => _interceptors;

    public NexarRequestBuilder Request() => new(this);

    /// <summary>
    /// Creates a new Nexar instance with custom configuration.
    /// </summary>
    public static Nexar Create(NexarConfig? config = null)
    {
        return new Nexar(config ?? new NexarConfig());
    }

    /// <summary>
    /// Creates a new Nexar instance with RequestOptions.
    /// </summary>
    public static Nexar Create(RequestOptions options)
    {
        var config = new NexarConfig
        {
            BaseUrl = options.BaseURL,
            TimeoutSeconds = options.Timeout.HasValue ? options.Timeout.Value / 1000 : 100,
            MaxRetryAttempts = options.MaxRetries ?? 0,
            ValidateSslCertificates = options.ValidateSsl ?? true
        };

        if (options.Headers != null)
        {
            config.DefaultHeaders = options.Headers;
        }

        return new Nexar(config);
    }

    // Static methods for convenient usage

    /// <summary>
    /// Sends a GET request and returns raw string response.
    /// </summary>
    public static async Task<string> Get(string url, RequestOptions? options = null)
    {
        var response = await DefaultInstance.GetAsync<string>(url, options?.Headers);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a GET request with typed response.
    /// </summary>
    public static Task<NexarResponse<T>> Get<T>(string url, RequestOptions? options = null)
    {
        return DefaultInstance.GetAsync<T>(url, options?.Headers);
    }

    /// <summary>
    /// Sends a POST request and returns raw string response.
    /// </summary>
    public static async Task<string> Post(string url, object? data = null, RequestOptions? options = null)
    {
        var response = await DefaultInstance.PostAsync<object, string>(url, options?.Headers, data);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a POST request with typed response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Post<TResponse>(string url, object? data = null, RequestOptions? options = null)
    {
        return DefaultInstance.PostAsync<object, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a POST request with typed request/response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Post<TRequest, TResponse>(string url, TRequest data, RequestOptions? options = null)
    {
        return DefaultInstance.PostAsync<TRequest, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a PUT request and returns raw string response.
    /// </summary>
    public static async Task<string> Put(string url, object? data = null, RequestOptions? options = null)
    {
        var response = await DefaultInstance.PutAsync<object, string>(url, options?.Headers, data);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a PUT request with typed response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Put<TResponse>(string url, object? data = null, RequestOptions? options = null)
    {
        return DefaultInstance.PutAsync<object, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a PUT request with typed request/response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Put<TRequest, TResponse>(string url, TRequest data, RequestOptions? options = null)
    {
        return DefaultInstance.PutAsync<TRequest, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a DELETE request and returns raw string response.
    /// </summary>
    public static async Task<string> Delete(string url, RequestOptions? options = null)
    {
        var response = await DefaultInstance.DeleteAsync<string>(url, options?.Headers);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a DELETE request with typed response.
    /// </summary>
    public static Task<NexarResponse<T>> Delete<T>(string url, RequestOptions? options = null)
    {
        return DefaultInstance.DeleteAsync<T>(url, options?.Headers);
    }

    /// <summary>
    /// Sends a PATCH request and returns raw string response.
    /// </summary>
    public static async Task<string> Patch(string url, object? data = null, RequestOptions? options = null)
    {
        var response = await DefaultInstance.PatchAsync<object, string>(url, options?.Headers, data);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a PATCH request with typed response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Patch<TResponse>(string url, object? data = null, RequestOptions? options = null)
    {
        return DefaultInstance.PatchAsync<object, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a PATCH request with typed request/response.
    /// </summary>
    public static Task<NexarResponse<TResponse>> Patch<TRequest, TResponse>(string url, TRequest data, RequestOptions? options = null)
    {
        return DefaultInstance.PatchAsync<TRequest, TResponse>(url, options?.Headers, data);
    }

    /// <summary>
    /// Sends a HEAD request and returns raw string response.
    /// </summary>
    public static async Task<string> Head(string url, RequestOptions? options = null)
    {
        var response = await DefaultInstance.HeadAsync<string>(url, options?.Headers);
        return response.RawContent;
    }

    /// <summary>
    /// Sends a HEAD request with typed response.
    /// </summary>
    public static Task<NexarResponse<T>> Head<T>(string url, RequestOptions? options = null)
    {
        return DefaultInstance.HeadAsync<T>(url, options?.Headers);
    }

    /// <summary>
    /// Sends a request with full configuration.
    /// </summary>
    public static async Task<NexarResponse<T>> Request<T>(RequestOptions options)
    {
        var instance = string.IsNullOrEmpty(options.BaseURL) ? DefaultInstance : Create(options);

        var url = options.Url ?? throw new ArgumentNullException(nameof(options.Url));
        var method = options.Method.ToUpperInvariant();

        return method switch
        {
            "GET" => await instance.GetAsync<T>(url, options.Headers),
            "POST" => await instance.PostAsync<object, T>(url, options.Headers, options.Data, options.ContentType),
            "PUT" => await instance.PutAsync<object, T>(url, options.Headers, options.Data, options.ContentType),
            "DELETE" => await instance.DeleteAsync<T>(url, options.Headers),
            "PATCH" => await instance.PatchAsync<object, T>(url, options.Headers, options.Data, options.ContentType),
            "HEAD" => await instance.HeadAsync<T>(url, options.Headers),
            _ => throw new NotSupportedException($"HTTP method {method} is not supported")
        };
    }

    public async Task<string> GetAsync(string url, Dictionary<string, string> headers)
    {
        var response = await GetAsync<string>(url, headers);
        return response.RawContent;
    }

    public async Task<NexarResponse<T>> GetAsync<T>(string url, Dictionary<string, string>? headers = null)
    {
        return await SendRequestAsync<T>(HttpMethod.Get, url, headers, null);
    }

    public async Task<string> PostAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var response = await PostAsync<T, string>(url, headers, body);
        return response.RawContent;
    }

    public async Task<NexarResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default)
    {
        return await SendRequestAsync<TResponse>(HttpMethod.Post, url, headers, body);
    }

    public async Task<NexarResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers,
        TRequest? body,
        ContentType contentType)
    {
        return await SendRequestAsync<TResponse>(HttpMethod.Post, url, headers, body, contentType);
    }

    public async Task<string> PutAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var response = await PutAsync<T, string>(url, headers, body);
        return response.RawContent;
    }

    public async Task<NexarResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default)
    {
        return await SendRequestAsync<TResponse>(HttpMethod.Put, url, headers, body);
    }

    public async Task<NexarResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers,
        TRequest? body,
        ContentType contentType)
    {
        return await SendRequestAsync<TResponse>(HttpMethod.Put, url, headers, body, contentType);
    }

    public async Task<string> DeleteAsync(string url, Dictionary<string, string> headers)
    {
        var response = await DeleteAsync<string>(url, headers);
        return response.RawContent;
    }

    public async Task<NexarResponse<T>> DeleteAsync<T>(string url, Dictionary<string, string>? headers = null)
    {
        return await SendRequestAsync<T>(HttpMethod.Delete, url, headers, null);
    }

    public async Task<string> PatchAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var response = await PatchAsync<T, string>(url, headers, body);
        return response.RawContent;
    }

    public async Task<NexarResponse<TResponse>> PatchAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers = null,
        TRequest? body = default)
    {
        return await SendRequestAsync<TResponse>(new HttpMethod("PATCH"), url, headers, body);
    }

    public async Task<NexarResponse<TResponse>> PatchAsync<TRequest, TResponse>(
        string url,
        Dictionary<string, string>? headers,
        TRequest? body,
        ContentType contentType)
    {
        return await SendRequestAsync<TResponse>(new HttpMethod("PATCH"), url, headers, body, contentType);
    }

    public async Task<string> HeadAsync(string url, Dictionary<string, string> headers)
    {
        var response = await HeadAsync<string>(url, headers);
        return response.RawContent;
    }

    public async Task<NexarResponse<T>> HeadAsync<T>(string url, Dictionary<string, string>? headers = null)
    {
        return await SendRequestAsync<T>(HttpMethod.Head, url, headers, null);
    }

    private HttpContent CreateHttpContent(object body, ContentType contentType)
    {
        switch (contentType)
        {
            case ContentType.Json:
                var json = JsonSerializer.Serialize(body);
                return new StringContent(json, Encoding.UTF8, "application/json");

            case ContentType.FormUrlEncoded:
                var formData = ConvertToKeyValuePairs(body);
                return new FormUrlEncodedContent(formData);

            case ContentType.FormData:
                var multipartContent = new MultipartFormDataContent();
                var formDataDict = ConvertToDictionary(body);

                foreach (var kvp in formDataDict)
                {
                    if (kvp.Value is byte[] byteArray)
                    {
                        var byteContent = new ByteArrayContent(byteArray);
                        multipartContent.Add(byteContent, kvp.Key, kvp.Key);
                    }
                    else if (kvp.Value is Stream stream)
                    {
                        var streamContent = new StreamContent(stream);
                        multipartContent.Add(streamContent, kvp.Key, kvp.Key);
                    }
                    else
                    {
                        multipartContent.Add(new StringContent(kvp.Value?.ToString() ?? string.Empty), kvp.Key);
                    }
                }
                return multipartContent;

            case ContentType.Binary:
                if (body is byte[] bytes)
                {
                    return new ByteArrayContent(bytes);
                }
                else if (body is Stream streamData)
                {
                    return new StreamContent(streamData);
                }
                else
                {
                    throw new ArgumentException("Binary content type requires byte[] or Stream data");
                }

            default:
                throw new NotSupportedException($"Content type {contentType} is not supported");
        }
    }

    private Dictionary<string, object> ConvertToDictionary(object body)
    {
        if (body is Dictionary<string, object> dictObj)
        {
            return dictObj;
        }
        else if (body is Dictionary<string, string> dictStr)
        {
            return dictStr.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
        }
        else
        {
            var json = JsonSerializer.Serialize(body);
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return dict ?? new Dictionary<string, object>();
        }
    }

    private IEnumerable<KeyValuePair<string, string>> ConvertToKeyValuePairs(object body)
    {
        if (body is Dictionary<string, string> dictStr)
        {
            return dictStr;
        }
        else if (body is Dictionary<string, object> dictObj)
        {
            return dictObj.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
        }
        else
        {
            var json = JsonSerializer.Serialize(body);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict ?? new Dictionary<string, string>();
        }
    }

    private async Task<NexarResponse<T>> SendRequestAsync<T>(
        HttpMethod method,
        string url,
        Dictionary<string, string>? headers,
        object? body,
        ContentType contentType = ContentType.Json)
    {
        var attempt = 0;
        var maxAttempts = _config.MaxRetryAttempts + 1;

        while (attempt < maxAttempts)
        {
            try
            {
                var request = new HttpRequestMessage(method, url);

                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                if (body != null)
                {
                    request.Content = CreateHttpContent(body, contentType);
                }

                request = await _interceptors.ExecuteRequestInterceptorsAsync(request);

                var response = await _client.SendAsync(request);
                response = await _interceptors.ExecuteResponseInterceptorsAsync(response);

                var rawContent = await response.Content.ReadAsStringAsync();

                var nexarResponse = new NexarResponse<T>
                {
                    RawContent = rawContent,
                    Status = (int)response.StatusCode,
                    StatusCode = response.StatusCode,
                    StatusText = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    IsSuccess = response.IsSuccessStatusCode,
                    Headers = response.Headers.ToDictionary(h => h.Key, h => h.Value)
                };

                if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(rawContent))
                {
                    try
                    {
                        if (typeof(T) == typeof(string))
                        {
                            nexarResponse.Data = (T)(object)rawContent;
                        }
                        else
                        {
                            nexarResponse.Data = JsonSerializer.Deserialize<T>(rawContent);
                        }
                    }
                    catch (JsonException)
                    {
                        nexarResponse.Data = default;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    nexarResponse.ErrorMessage = $"Request failed with status code {response.StatusCode}";
                }

                return nexarResponse;
            }
            catch (Exception ex)
            {
                await _interceptors.ExecuteErrorInterceptorsAsync(ex);

                attempt++;
                if (attempt >= maxAttempts)
                {
                    return new NexarResponse<T>
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        Exception = ex,
                        Status = 500,
                        StatusCode = HttpStatusCode.InternalServerError,
                        StatusText = "Internal Server Error"
                    };
                }

                var delay = _config.UseExponentialBackoff
                    ? _config.RetryDelayMilliseconds * (int)Math.Pow(2, attempt - 1)
                    : _config.RetryDelayMilliseconds;

                await Task.Delay(delay);
            }
        }

        return new NexarResponse<T>
        {
            IsSuccess = false,
            ErrorMessage = "Request failed after all retry attempts",
            Status = 500,
            StatusCode = HttpStatusCode.InternalServerError,
            StatusText = "Internal Server Error"
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
