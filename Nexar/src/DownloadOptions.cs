namespace Nexar;

/// <summary>
/// Options for a parallel download with <see cref="RequestBuilder.DownloadTo(string, DownloadOptions, CancellationToken)"/>.
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>How many ranges are fetched at the same time. Defaults to 4.</summary>
    public int Connections { get; init; } = 4;

    /// <summary>The size of each range. Defaults to 8 MB.</summary>
    public long ChunkSize { get; init; } = 8 * 1024 * 1024;
}
