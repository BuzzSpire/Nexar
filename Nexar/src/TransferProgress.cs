namespace Nexar;

/// <summary>
/// Progress of an upload or download, reported through <see cref="RequestBuilder.UploadProgress"/> and
/// <see cref="RequestBuilder.DownloadProgress"/>.
/// </summary>
/// <param name="BytesTransferred">Bytes sent or received so far.</param>
/// <param name="TotalBytes">The total, when the length is known.</param>
public readonly record struct TransferProgress(long BytesTransferred, long? TotalBytes)
{
    /// <summary>The fraction done, from 0 to 1, when the total is known.</summary>
    public double? Percent => TotalBytes is > 0 ? (double)BytesTransferred / TotalBytes.Value : null;
}
