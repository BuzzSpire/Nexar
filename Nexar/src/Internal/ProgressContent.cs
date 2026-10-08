using System.Net;

namespace Nexar;

/// <summary>
/// Wraps a request body and reports how many bytes have been written to the network.
/// </summary>
internal sealed class UploadProgressContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly IProgress<TransferProgress> _progress;

    public UploadProgressContent(HttpContent inner, IProgress<TransferProgress> progress)
    {
        _inner = inner;
        _progress = progress;
        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var counting = new CountingStream(stream, _progress, _inner.Headers.ContentLength, countWrites: true);
        await _inner.CopyToAsync(counting, cancellationToken).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _inner.Headers.ContentLength ?? 0;
        return _inner.Headers.ContentLength != null;
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

/// <summary>
/// Wraps a response body and reports how many bytes have been read from the network.
/// </summary>
internal sealed class DownloadProgressContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly IProgress<TransferProgress> _progress;
    private readonly long? _total;

    public DownloadProgressContent(HttpContent inner, IProgress<TransferProgress> progress)
    {
        _inner = inner;
        _progress = progress;
        // Read the declared length before anything buffers the body, which would make it "known".
        _total = inner.Headers.ContentLength;
        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new CountingStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _progress, _total, countWrites: false);

    protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var source = await CreateContentReadStreamAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _total ?? 0;
        return _total != null;
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

/// <summary>
/// Passes reads or writes through and reports the running total.
/// </summary>
internal sealed class CountingStream(Stream inner, IProgress<TransferProgress> progress, long? total, bool countWrites) : Stream
{
    private long _transferred;

    public override bool CanRead => !countWrites && inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => countWrites && inner.CanWrite;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _transferred;
        set => throw new NotSupportedException();
    }

    private void Report(int count)
    {
        if (count > 0)
        {
            _transferred += count;
            progress.Report(new TransferProgress(_transferred, total));
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        Report(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(buffer.Length);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // Upload streams belong to the transport; only a download source is ours to dispose.
        if (disposing && !countWrites)
        {
            inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
