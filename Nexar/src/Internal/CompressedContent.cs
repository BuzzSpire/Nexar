using System.IO.Compression;
using System.Net;

namespace Nexar;

/// <summary>
/// The encodings <see cref="RequestBuilder.Compress"/> can apply to a request body.
/// </summary>
public enum ContentEncoding
{
    /// <summary><c>Content-Encoding: gzip</c></summary>
    Gzip,

    /// <summary><c>Content-Encoding: deflate</c> (zlib format, as HTTP defines it)</summary>
    Deflate,

    /// <summary><c>Content-Encoding: br</c></summary>
    Brotli
}

/// <summary>
/// Compresses another content while it is sent. The length is unknown, so it goes out chunked.
/// </summary>
internal sealed class CompressedContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly ContentEncoding _encoding;

    public CompressedContent(HttpContent inner, ContentEncoding encoding)
    {
        _inner = inner;
        _encoding = encoding;
        foreach (var header in inner.Headers.Where(h => !h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        Headers.ContentEncoding.Add(encoding switch
        {
            ContentEncoding.Gzip => "gzip",
            ContentEncoding.Deflate => "deflate",
            _ => "br"
        });
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using Stream compressor = _encoding switch
        {
            ContentEncoding.Gzip => new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true),
            ContentEncoding.Deflate => new ZLibStream(stream, CompressionLevel.Fastest, leaveOpen: true),
            _ => new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true)
        };
        await _inner.CopyToAsync(compressor, cancellationToken).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
