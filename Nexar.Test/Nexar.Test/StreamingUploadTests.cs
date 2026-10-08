using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Nexar.Testing;

namespace Nexar.Test;

public class StreamingUploadTests
{
    private record Event(int Id, string Name);

    private static async IAsyncEnumerable<Event> Events(int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new Event(i, $"event-{i}");
        }
    }

    [Fact]
    public async Task JsonLinesUploadsEveryItemChunked()
    {
        await using var server = TestServer.Start(r => ServerResponse.Text($"{r.Header("Transfer-Encoding")}|{r.Header("Content-Type")}|{r.BodyText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var reply = await client.Post("/bulk").JsonLines(Events(5000)).Send().ErrorForStatus().Text();

        Assert.Equal("chunked|application/x-ndjson|5000", reply);
        var lines = server.Requests.Single().BodyText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("{\"id\":1,\"name\":\"event-1\"}", lines[0]);
        Assert.Equal("{\"id\":5000,\"name\":\"event-5000\"}", lines[^1]);
    }

    [Fact]
    public async Task JsonLinesIsNeverRetried()
    {
        var mock = new MockHttp();
        mock.OnPost("/bulk").Respond(HttpStatusCode.ServiceUnavailable).Times(1);
        using var client = mock.CreateClient(configure: b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Post("/bulk").JsonLines(Events(3)).Retryable().Send();

        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task JsonLinesWithCompressionAndProgress()
    {
        await using var server = TestServer.Start(r =>
        {
            using var gzip = new GZipStream(new MemoryStream(r.Body), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return ServerResponse.Text(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ToString());
        });
        long sent = 0;
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var count = await client.Post("/bulk")
            .JsonLines(Events(1000))
            .Compress()
            .UploadProgress(new SyncProgress(p => sent = p.BytesTransferred))
            .Send()
            .Text();

        Assert.Equal("1000", count);
        Assert.Equal(server.Requests.Single().Body.Length, sent);
    }

    [Fact]
    public async Task JsonStreamedSendsLargeObjectsAndCanBeRetried()
    {
        var big = Enumerable.Range(0, 20_000).Select(i => new Event(i, new string('x', 20))).ToList();
        var mock = new MockHttp();
        mock.OnPost("/import").Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK);
        using var client = mock.CreateClient(configure: b => b.Retry(1, TimeSpan.Zero));

        using var res = await client.Post("/import").JsonStreamed(big).Retryable().Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, mock.Requests.Count);
        Assert.All(mock.Requests, r =>
            Assert.Equal(20_000, JsonSerializer.Deserialize<List<Event>>(r.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Count));
        Assert.StartsWith("application/json", mock.Requests[0].Header("Content-Type"));
    }

    [Fact]
    public async Task CancellingStopsTheUpload()
    {
        await using var server = TestServer.Start(_ => ServerResponse.Text("ok"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        static async IAsyncEnumerable<Event> Endless([EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; ; i++)
            {
                await Task.Delay(10, ct);
                yield return new Event(i, "x");
            }
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Post("/bulk").JsonLines(Endless()).Send(cts.Token));
    }

    private sealed class SyncProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }
}
