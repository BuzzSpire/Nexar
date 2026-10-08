using System.Net;
using System.Text;

namespace Nexar.Test;

public sealed class FileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nexar-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    // ---- Uploads -------------------------------------------------------------

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("data.JSON", "application/json")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("archive.unknownext", "application/octet-stream")]
    public async Task FileBodyDetectsContentType(string name, string expected)
    {
        var path = PathOf(name);
        await File.WriteAllTextAsync(path, "content");
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Put("/files").File(path).Send();

        Assert.Equal(expected, handler.Last.ContentHeaders["Content-Type"]);
        Assert.Equal("7", handler.Last.ContentHeaders["Content-Length"]);
        Assert.Equal("content", handler.Last.Body);
    }

    [Fact]
    public async Task FileBodyContentTypeCanBeOverridden()
    {
        var path = PathOf("x.bin");
        await File.WriteAllTextAsync(path, "x");
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Put("/").File(path, "application/vnd.custom").Send();

        Assert.Equal("application/vnd.custom", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task FileBodyIsRetried()
    {
        var path = PathOf("retry.txt");
        await File.WriteAllTextAsync(path, "payload");
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK });
        var handler = new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue()));
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero));

        using var res = await client.Put("/").File(path).Send();

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("payload", r.Body));
    }

    [Fact]
    public async Task MissingFileIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Put("/").File(PathOf("missing.txt")).Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task FileDeletedAfterBuildIsBuilderError()
    {
        var path = PathOf("gone.txt");
        await File.WriteAllTextAsync(path, "x");
        using var client = TestClient.Create(new FakeHandler());
        var builder = client.Put("/").File(path);
        File.Delete(path);

        var ex = await Assert.ThrowsAsync<NexarException>(() => builder.Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task MultipartFileFromPath()
    {
        var path = PathOf("avatar.png");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/upload").Multipart(new MultipartForm().Text("userId", "1").File("image", path)).Send();

        Assert.Contains("filename=avatar.png", handler.Last.Body);
        Assert.Contains("image/png", handler.Last.Body);
    }

    [Fact]
    public void MultipartMissingFileThrows()
    {
        Assert.Throws<FileNotFoundException>(() => new MultipartForm().File("f", PathOf("missing.png")));
    }

    // ---- SaveTo --------------------------------------------------------------

    [Fact]
    public async Task SaveToWritesTheBody()
    {
        var path = PathOf("out.bin");
        using var client = TestClient.Create(new FakeHandler(body: "downloaded", mediaType: "text/plain"));

        await client.Get("/file").Send().ErrorForStatus().SaveTo(path);

        Assert.Equal("downloaded", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task SaveToReplacesExistingFile()
    {
        var path = PathOf("out.txt");
        await File.WriteAllTextAsync(path, "old content that is longer");
        using var client = TestClient.Create(new FakeHandler(body: "new", mediaType: "text/plain"));

        await client.Get("/file").Send().SaveTo(path);

        Assert.Equal("new", await File.ReadAllTextAsync(path));
    }

    /// <summary>Delivers some bytes, then fails like a dropped connection.</summary>
    private sealed class BrokenContent(byte[] prefix) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded, "connection dropped");

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new BrokenStream(prefix));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BrokenStream(byte[] prefix) : MemoryStream(prefix)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new HttpIOException(HttpRequestError.ResponseEnded, "connection dropped");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
    }

    private static FakeHandler Broken(string prefix) => new(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent(Encoding.ASCII.GetBytes(prefix)) });

    [Fact]
    public async Task FailedSaveLeavesNoFile()
    {
        var path = PathOf("out.bin");
        using var client = TestClient.Create(Broken("partial"));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/file").Send().SaveTo(path));

        Assert.Equal(ErrorKind.Body, ex.Kind);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    // ---- DownloadTo with resume ----------------------------------------------

    [Fact]
    public async Task DownloadToWithoutResume()
    {
        var path = PathOf("file.txt");
        var handler = RangeTests.RangeServer();
        using var client = TestClient.Create(handler);

        var size = await client.Get("/file").DownloadTo(path);

        Assert.Equal(20, size);
        Assert.Equal(RangeTests.Resource, await File.ReadAllTextAsync(path));
        Assert.False(handler.Last.Headers.ContainsKey("Range"));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task DownloadToResumesFromPartialFile()
    {
        var path = PathOf("file.txt");
        await File.WriteAllTextAsync(path + ".partial", RangeTests.Resource[..8]);
        var handler = RangeTests.RangeServer();
        using var client = TestClient.Create(handler);

        await client.Get("/file").IfRange(RangeTests.ResourceETag).DownloadTo(path, resume: true);

        Assert.Equal("bytes=8-", handler.Last.Headers["Range"]);
        Assert.Equal(RangeTests.Resource, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task DownloadToRestartsWhenServerIgnoresTheRange()
    {
        var path = PathOf("file.txt");
        await File.WriteAllTextAsync(path + ".partial", "STALE-DATA");
        using var client = TestClient.Create(RangeTests.RangeServer());

        // The ETag changed, so the server sends the whole resource with 200.
        await client.Get("/file").IfRange("old-etag").DownloadTo(path, resume: true);

        Assert.Equal(RangeTests.Resource, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DownloadToRejectsAMismatchedRange()
    {
        var path = PathOf("file.txt");
        await File.WriteAllTextAsync(path + ".partial", "01234");
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("xyz"u8.ToArray()) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 2, 20);
            return response;
        });
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/file").DownloadTo(path, resume: true));

        Assert.Equal(ErrorKind.Body, ex.Kind);
        Assert.False(File.Exists(path));
        Assert.Equal("01234", await File.ReadAllTextAsync(path + ".partial"));
    }

    [Fact]
    public async Task FailedResumableDownloadKeepsPartialFileForNextTime()
    {
        var path = PathOf("file.txt");
        using var brokenClient = TestClient.Create(Broken(RangeTests.Resource[..12]));
        var resumable = RangeTests.RangeServer();
        using var resumeClient = TestClient.Create(resumable);

        await Assert.ThrowsAsync<NexarException>(() => brokenClient.Get("/file").DownloadTo(path, resume: true));
        Assert.Equal(RangeTests.Resource[..12], await File.ReadAllTextAsync(path + ".partial"));

        await resumeClient.Get("/file").DownloadTo(path, resume: true);

        Assert.Equal("bytes=12-", resumable.Last.Headers["Range"]);
        Assert.Equal(RangeTests.Resource, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FailedDownloadWithoutResumeLeavesNothing()
    {
        var path = PathOf("file.txt");
        using var client = TestClient.Create(Broken("abc"));

        await Assert.ThrowsAsync<NexarException>(() => client.Get("/file").DownloadTo(path));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadToErrorStatusThrows()
    {
        var path = PathOf("file.txt");
        using var client = TestClient.Create(new FakeHandler(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/missing").DownloadTo(path));

        Assert.True(ex.IsStatus);
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
