using System.Net;

namespace Nexar.Test;

public sealed class ProgressTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nexar-progress-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Records reports synchronously (Progress&lt;T&gt; would post them to the thread pool).</summary>
    private sealed class Recorder : IProgress<TransferProgress>
    {
        public List<TransferProgress> Reports { get; } = new();

        public void Report(TransferProgress value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }

    private sealed class StreamedContent(int size) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[size]).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task UploadProgressReachesTheTotal()
    {
        var recorder = new Recorder();
        using var client = TestClient.Create(new FakeHandler());

        await client.Put("/upload").Body(new byte[300_000]).UploadProgress(recorder).Send();

        Assert.NotEmpty(recorder.Reports);
        Assert.Equal(new TransferProgress(300_000, 300_000), recorder.Reports[^1]);
        Assert.Equal(1.0, recorder.Reports[^1].Percent);
        Assert.True(recorder.Reports.Zip(recorder.Reports.Skip(1)).All(p => p.First.BytesTransferred < p.Second.BytesTransferred));
    }

    [Fact]
    public async Task UploadProgressOverRealSocketsCountsCompressedBytes()
    {
        await using var server = TestServer.Start(request => ServerResponse.Text(request.Body.Length.ToString()));
        var recorder = new Recorder();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var received = await client.Post("/").Body(new byte[500_000]).Compress().UploadProgress(recorder).Send().Text();

        Assert.Equal(received, recorder.Reports[^1].BytesTransferred.ToString());
        Assert.Null(recorder.Reports[^1].TotalBytes);   // compressed length is unknown up front
    }

    [Fact]
    public async Task DownloadProgressWithKnownLength()
    {
        var recorder = new Recorder();
        using var client = TestClient.Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[250_000])
        }));

        var bytes = await client.Get("/file").DownloadProgress(recorder).Send().Bytes();

        Assert.Equal(250_000, bytes.Length);
        Assert.Equal(new TransferProgress(250_000, 250_000), recorder.Reports[^1]);
    }

    [Fact]
    public async Task DownloadProgressWithUnknownLength()
    {
        var recorder = new Recorder();
        using var client = TestClient.Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamedContent(100_000) }));

        await client.Get("/").DownloadProgress(recorder).Send().Bytes();

        Assert.Equal(new TransferProgress(100_000, null), recorder.Reports[^1]);
        Assert.Null(recorder.Reports[^1].Percent);
    }

    [Fact]
    public async Task DownloadProgressForSaveToAndStream()
    {
        var path = Path.Combine(_dir, "out.bin");
        var saveRecorder = new Recorder();
        var streamRecorder = new Recorder();
        using var client = TestClient.Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[200_000])
        }));

        await client.Get("/").DownloadProgress(saveRecorder).Send().SaveTo(path);
        await using (var body = await client.Get("/").DownloadProgress(streamRecorder).Send().Stream())
        {
            await body.CopyToAsync(Stream.Null);
        }

        Assert.Equal(200_000, saveRecorder.Reports[^1].BytesTransferred);
        Assert.Equal(200_000, streamRecorder.Reports[^1].BytesTransferred);
        Assert.Equal(200_000, new FileInfo(path).Length);
    }

    [Fact]
    public async Task ContentHeadersSurviveTheWrappers()
    {
        var handler = new FakeHandler(body: "{\"a\":1}");
        using var client = TestClient.Create(handler);

        using var res = await client.Post("/").Json(new { a = 1 }).UploadProgress(new Recorder()).DownloadProgress(new Recorder()).Send();

        Assert.Equal("application/json; charset=utf-8", handler.Last.ContentHeaders["Content-Type"]);
        Assert.Equal("application/json", res.ContentType!.MediaType);
        Assert.Equal("{\"a\":1}", await res.Text());
    }
}
