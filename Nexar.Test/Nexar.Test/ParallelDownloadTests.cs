using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nexar.Test;

public sealed class ParallelDownloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nexar-parallel-").FullName;
    private static readonly string Resource = string.Concat(Enumerable.Range(0, 20_000).Select(i => (char)('a' + i % 26)));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private sealed class Recorder : IProgress<TransferProgress>
    {
        public long Last;
        public void Report(TransferProgress value)
        {
            lock (this)
            {
                Last = Math.Max(Last, value.BytesTransferred);
            }
        }
    }

    [Fact]
    public async Task DownloadsRangesInParallelByteForByte()
    {
        var handler = RangeTests.RangeServer(Resource);
        var progress = new Recorder();
        using var client = TestClient.Create(handler);
        var path = PathOf("file.bin");

        var size = await client.Get("/file").DownloadProgress(progress).DownloadTo(path, new DownloadOptions { Connections = 3, ChunkSize = 3000 });

        Assert.Equal(Resource.Length, size);
        Assert.Equal(Resource, await File.ReadAllTextAsync(path));
        Assert.Equal(1 + 7, handler.Requests.Count);   // the probe plus ceil(20000 / 3000) ranges
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("\"r1\"", r.Headers["If-Range"]));
        Assert.Equal(Resource.Length, progress.Last);
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task FallsBackWhenRangesAreIgnored()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Resource) });
        using var client = TestClient.Create(handler);
        var path = PathOf("file.bin");

        await client.Get("/file").DownloadTo(path, new DownloadOptions { Connections = 4, ChunkSize = 1000 });

        Assert.Equal(Resource, await File.ReadAllTextAsync(path));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ChangedResourceFailsAndLeavesNothing()
    {
        var calls = 0;
        var handler = new FakeHandler(request =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                var probe = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("a"u8.ToArray()) };
                probe.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 5000);
                probe.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                return probe;
            }
            // The ETag no longer matches, so If-Range makes the server send the whole (new) file.
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('z', 6000)) };
        });
        using var client = TestClient.Create(handler);
        var path = PathOf("file.bin");

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/file").DownloadTo(path, new DownloadOptions { ChunkSize = 1000 }));

        Assert.Equal(ErrorKind.Body, ex.Kind);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task WorksOverRealSockets()
    {
        await using var server = TestServer.Start(request =>
        {
            var bytes = Encoding.ASCII.GetBytes(Resource);
            if (request.Header("Range") is { } range)
            {
                var parts = range["bytes=".Length..].Split('-');
                var from = int.Parse(parts[0]);
                var to = Math.Min(int.Parse(parts[1]), bytes.Length - 1);
                return new ServerResponse
                {
                    Status = 206,
                    Reason = "Partial Content",
                    Body = bytes[from..(to + 1)],
                    Headers = [("Content-Range", $"bytes {from}-{to}/{bytes.Length}"), ("ETag", "\"s1\"")]
                };
            }
            return new ServerResponse { Body = bytes };
        });
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();
        var path = PathOf("socket.bin");

        await client.Get("/big").DownloadTo(path, new DownloadOptions { Connections = 4, ChunkSize = 2048 });

        Assert.Equal(Resource, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task OnlyGetRequestsCanBeDownloadedInParallel()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Post("/").DownloadTo(PathOf("x"), new DownloadOptions()));

        Assert.True(ex.IsBuilder);
    }
}
