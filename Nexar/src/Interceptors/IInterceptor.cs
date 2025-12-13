namespace Nexar.Interceptors;

/// <summary>
/// Interface for HTTP request/response interceptors.
/// </summary>
public interface IInterceptor
{
    /// <summary>
    /// Intercepts the request before it is sent.
    /// </summary>
    Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request);

    /// <summary>
    /// Intercepts the response after it is received.
    /// </summary>
    Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response);

    /// <summary>
    /// Handles errors that occur during the request.
    /// </summary>
    Task OnErrorAsync(Exception exception);
}
