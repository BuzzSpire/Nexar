namespace Nexar.Configuration;

/// <summary>
/// Configuration options for Nexar HTTP client.
/// </summary>
public class NexarConfig
{
    /// <summary>
    /// Base URL for all requests.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Default headers to be added to every request.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>
    /// Request timeout in milliseconds. Default is 100000ms (100 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = 100_000;

    /// <summary>
    /// Maximum number of retry attempts for failed requests. Default is 0 (no retry).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 0;

    /// <summary>
    /// Delay between retry attempts in milliseconds. Default is 1000ms.
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Whether to use exponential backoff for retries. Default is true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Validate SSL certificates. Default is true.
    /// </summary>
    public bool ValidateSslCertificates { get; set; } = true;
}
