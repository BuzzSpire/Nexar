using System.Net;
using System.Net.Http.Headers;

namespace Nexar;

/// <summary>
/// A body written straight to the network by a callback, without buffering; sent chunked.
/// </summary>
internal sealed class PushContent : HttpContent
{
    private readonly Func<Stream, CancellationToken, Task> _write;

    public PushContent(Func<Stream, CancellationToken, Task> write, string mediaType)
    {
        _write = write;
        Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        _write(stream, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
        _write(stream, cancellationToken);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
