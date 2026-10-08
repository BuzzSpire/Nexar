using System.IO.Compression;
using System.Net;
using System.Text;

namespace Nexar.Test;

public sealed class SizeAndCompressionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nexar-size-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- #67 Maximum response size ------------------------------------------------

    /// <summary>A body of <paramref name="size"/> bytes, optionally without Content-Length (streamed).</summary>
    private sealed class SizedContent(int size, bool declareLength) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[size]).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = size;
            return declareLength;
        }
    }

    private static FakeHandler Sized(int size, bool declareLength) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new SizedContent(size, declareLength) });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedBodyIsBodyError(bool declareLength)
    {
        using var client = TestClient.Create(Sized(2000, declareLength), b => b.MaxResponseSize(1000));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().Bytes());

        Assert.Equal(ErrorKind.Body, ex.Kind);
        Assert.Contains("1000", ex.Message);
    }

    [Fact]
    public async Task BodyWithinLimitIsRead()
    {
        using var client = TestClient.Create(Sized(1000, declareLength: false), b => b.MaxResponseSize(1000));

        var bytes = await client.Get("/").Send().Bytes();

        Assert.Equal(1000, bytes.Length);
    }

    [Fact]
    public async Task RequestLimitOverridesClientLimit()
    {
        using var client = TestClient.Create(Sized(2000, declareLength: true), b => b.MaxResponseSize(100));

        var bytes = await client.Get("/").MaxResponseSize(5000).Send().Bytes();
        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").MaxResponseSize(10).Send().Text());

        Assert.Equal(2000, bytes.Length);
        Assert.Equal(ErrorKind.Body, ex.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveToHonorsTheLimit(bool declareLength)
    {
        var path = Path.Combine(_dir, "big.bin");
        using var client = TestClient.Create(Sized(200_000, declareLength), b => b.MaxResponseSize(100_000));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().SaveTo(path));

        Assert.Equal(ErrorKind.Body, ex.Kind);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task NoLimitByDefault()
    {
        using var client = TestClient.Create(Sized(5_000_000, declareLength: false));

        var bytes = await client.Get("/").Send().Bytes();

        Assert.Equal(5_000_000, bytes.Length);
    }

    // ---- #84 Request body compression ---------------------------------------------

    private static (FakeHandler Handler, List<(byte[] Body, string? Encoding, string? Type)> Received) CapturingHandler()
    {
        var received = new List<(byte[] Body, string? Encoding, string? Type)>();
        var handler = new FakeHandler(async (request, ct) =>
        {
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            received.Add((body, request.Content.Headers.ContentEncoding.SingleOrDefault(), request.Content.Headers.ContentType?.MediaType));
            return FakeHandler.Respond(HttpStatusCode.OK);
        });
        return (handler, received);
    }

    private static string Decompress(byte[] body, ContentEncoding encoding)
    {
        using var input = new MemoryStream(body);
        using Stream decompressor = encoding switch
        {
            ContentEncoding.Gzip => new GZipStream(input, CompressionMode.Decompress),
            ContentEncoding.Deflate => new ZLibStream(input, CompressionMode.Decompress),
            _ => new BrotliStream(input, CompressionMode.Decompress)
        };
        using var reader = new StreamReader(decompressor, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(ContentEncoding.Gzip, "gzip")]
    [InlineData(ContentEncoding.Deflate, "deflate")]
    [InlineData(ContentEncoding.Brotli, "br")]
    public async Task CompressesTheBody(ContentEncoding encoding, string header)
    {
        var (handler, received) = CapturingHandler();
        using var client = TestClient.Create(handler);
        var events = Enumerable.Range(0, 500).Select(i => new { id = i, message = "the same log line again" }).ToArray();

        await client.Post("/ingest").Json(events).Compress(encoding).Send();

        var (body, sentEncoding, type) = Assert.Single(received);
        Assert.Equal(header, sentEncoding);
        Assert.Equal("application/json", type);
        var json = Decompress(body, encoding);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(events), json);
        Assert.True(body.Length < Encoding.UTF8.GetByteCount(json) / 5, $"{body.Length} bytes compressed");
    }

    [Fact]
    public async Task CompressedBodyIsRetried()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK });
        var received = new List<string>();
        var handler = new FakeHandler(async (request, ct) =>
        {
            received.Add(Decompress(await request.Content!.ReadAsByteArrayAsync(ct), ContentEncoding.Gzip));
            return FakeHandler.Respond(statuses.Dequeue());
        });
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero));

        using var res = await client.Put("/").Body("payload").Compress().Send();

        Assert.Equal(new[] { "payload", "payload" }, received);
    }

    [Fact]
    public async Task CompressWithoutBodyDoesNothing()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").Compress().Send();

        Assert.Null(handler.Last.Body);
    }
}
