namespace Nexar.Interceptors;

/// <summary>
/// Collection of interceptors.
/// </summary>
public class InterceptorCollection
{
    private readonly List<IInterceptor> _interceptors = new();

    /// <summary>
    /// Adds an interceptor to the collection.
    /// </summary>
    public void Add(IInterceptor interceptor)
    {
        _interceptors.Add(interceptor);
    }

    /// <summary>
    /// Removes an interceptor from the collection.
    /// </summary>
    public void Remove(IInterceptor interceptor)
    {
        _interceptors.Remove(interceptor);
    }

    /// <summary>
    /// Clears all interceptors.
    /// </summary>
    public void Clear()
    {
        _interceptors.Clear();
    }

    /// <summary>
    /// Executes all request interceptors.
    /// </summary>
    public async Task<HttpRequestMessage> ExecuteRequestInterceptorsAsync(HttpRequestMessage request)
    {
        var currentRequest = request;
        foreach (var interceptor in _interceptors)
        {
            currentRequest = await interceptor.OnRequestAsync(currentRequest);
        }
        return currentRequest;
    }

    /// <summary>
    /// Executes all response interceptors.
    /// </summary>
    public async Task<HttpResponseMessage> ExecuteResponseInterceptorsAsync(HttpResponseMessage response)
    {
        var currentResponse = response;
        foreach (var interceptor in _interceptors)
        {
            currentResponse = await interceptor.OnResponseAsync(currentResponse);
        }
        return currentResponse;
    }

    /// <summary>
    /// Executes all error interceptors.
    /// </summary>
    public async Task ExecuteErrorInterceptorsAsync(Exception exception)
    {
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnErrorAsync(exception);
        }
    }
}
