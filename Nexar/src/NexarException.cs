using System.Net;
using System.Text.Json;

namespace Nexar;

/// <summary>
/// The category of a <see cref="NexarException"/>.
/// </summary>
public enum ErrorKind
{
    /// <summary>The request could not be built (invalid URL, serialization failure, ...).</summary>
    Builder,

    /// <summary>The connection to the server could not be established.</summary>
    Connect,

    /// <summary>The request timed out before response headers were received.</summary>
    Timeout,

    /// <summary>The request failed while being sent.</summary>
    Request,

    /// <summary>The server returned a 4xx or 5xx status (raised by <c>ErrorForStatus()</c>).</summary>
    Status,

    /// <summary>The response body could not be read.</summary>
    Body,

    /// <summary>The response body could not be decoded.</summary>
    Decode,

    /// <summary>Credentials could not be obtained or applied (e.g. the OAuth token endpoint failed).</summary>
    Auth,

    /// <summary>A redirect was not followed: too many hops, or a redirect from HTTPS to HTTP.</summary>
    Redirect
}

/// <summary>
/// The single exception type thrown by Nexar.
/// </summary>
/// <remarks>
/// Cancellation through a caller-supplied <see cref="CancellationToken"/> is not wrapped
/// and surfaces as <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class NexarException : Exception
{
    internal NexarException(
        ErrorKind kind,
        string message,
        Uri? url = null,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Url = url;
        StatusCode = statusCode;
    }

    /// <summary>The category of the error.</summary>
    public ErrorKind Kind { get; }

    /// <summary>The URL of the request, if known.</summary>
    public Uri? Url { get; }

    /// <summary>The response status code. Only set when <see cref="Kind"/> is <see cref="ErrorKind.Status"/>.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// The start of the error response body (up to 64 KB), set by <c>ErrorForStatus()</c>.
    /// Null if the body could not be read.
    /// </summary>
    public string? ResponseBody { get; internal init; }

    /// <summary>True if <see cref="ResponseBody"/> was cut off at 64 KB.</summary>
    public bool IsResponseBodyTruncated { get; internal init; }

    /// <summary>The error response headers, including content headers. Set by <c>ErrorForStatus()</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ResponseHeaders { get; internal init; }

    /// <summary>
    /// The RFC 9457 problem document, when the error response was <c>application/problem+json</c>.
    /// </summary>
    public NexarProblemDetails? Problem { get; internal init; }

    internal JsonSerializerOptions? JsonOptions { get; init; }

    /// <summary>
    /// Deserializes <see cref="ResponseBody"/> with the client's JSON options,
    /// e.g. <c>e.Json&lt;ProblemDetails&gt;()</c>. Returns <c>default</c> if there is no body or it is not valid JSON.
    /// </summary>
    public T? Json<T>()
    {
        if (string.IsNullOrEmpty(ResponseBody) || IsResponseBodyTruncated)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(ResponseBody, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>True if the request could not be built.</summary>
    public bool IsBuilder => Kind == ErrorKind.Builder;

    /// <summary>True if the connection could not be established.</summary>
    public bool IsConnect => Kind == ErrorKind.Connect;

    /// <summary>True if the request timed out.</summary>
    public bool IsTimeout => Kind == ErrorKind.Timeout;

    /// <summary>True if the error was raised by <c>ErrorForStatus()</c>.</summary>
    public bool IsStatus => Kind == ErrorKind.Status;

    /// <summary>True if the response body could not be decoded.</summary>
    public bool IsDecode => Kind == ErrorKind.Decode;

    /// <summary>True if credentials could not be obtained or applied.</summary>
    public bool IsAuth => Kind == ErrorKind.Auth;

    /// <summary>True if a redirect was not followed.</summary>
    public bool IsRedirect => Kind == ErrorKind.Redirect;
}
