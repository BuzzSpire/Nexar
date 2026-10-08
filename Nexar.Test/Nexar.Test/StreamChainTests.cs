using System.Net;

namespace Nexar.Test;

public class StreamChainTests
{
    private sealed class TrackingContent(byte[] data) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(data).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = data.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task StreamCanBeChainedOntoSend()
    {
        var content = new TrackingContent("streamed body"u8.ToArray());
        using var client = TestClient.Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        await using (var body = await client.Get("/big.zip").Send().ErrorForStatus().Stream())
        {
            using var reader = new StreamReader(body, leaveOpen: true);
            Assert.Equal("streamed body", await reader.ReadToEndAsync());
            Assert.False(content.Disposed);
        }

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task SyncDisposeAlsoReleasesTheResponse()
    {
        var content = new TrackingContent("x"u8.ToArray());
        using var client = TestClient.Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var body = await client.Get("/").Send().Stream();
        body.Dispose();

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task CopyToWorks()
    {
        using var client = TestClient.Create(new FakeHandler(body: new string('a', 100_000), mediaType: "text/plain"));

        await using var body = await client.Get("/").Send().Stream();
        using var target = new MemoryStream();
        await body.CopyToAsync(target);

        Assert.Equal(100_000, target.Length);
    }

    [Fact]
    public async Task StreamIsReadOnly()
    {
        using var client = TestClient.Create(new FakeHandler(body: "x"));

        await using var body = await client.Get("/").Send().Stream();

        Assert.True(body.CanRead);
        Assert.False(body.CanWrite);
        Assert.Throws<NotSupportedException>(() => body.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ErrorStatusIsReportedBeforeStreaming()
    {
        using var client = TestClient.Create(new FakeHandler(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().ErrorForStatus().Stream());

        Assert.True(ex.IsStatus);
    }
}
