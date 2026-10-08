using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nexar.Test;

public class RangeTests
{
    internal const string Resource = "0123456789abcdefghij";
    internal static readonly EntityTagHeaderValue ResourceETag = new("\"r1\"");

    /// <summary>Serves <see cref="Resource"/> with single-range support and If-Range on the ETag.</summary>
    internal static FakeHandler RangeServer(string resource = Resource) => new(request =>
    {
        var bytes = Encoding.ASCII.GetBytes(resource);
        var range = request.Headers.Range?.Ranges.SingleOrDefault();
        var ifRange = request.Headers.IfRange?.EntityTag;
        var honor = range != null && (ifRange == null || ifRange.Tag == ResourceETag.Tag);

        HttpResponseMessage response;
        if (!honor)
        {
            response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
        else
        {
            long from, to;
            if (range!.From is null)
            {
                from = bytes.Length - range.To!.Value;
                to = bytes.Length - 1;
            }
            else
            {
                from = range.From.Value;
                to = Math.Min(range.To ?? bytes.Length - 1, bytes.Length - 1);
            }
            if (from >= bytes.Length)
            {
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            }
            response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[(int)from..((int)to + 1)])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
        }
        response.Headers.ETag = ResourceETag;
        response.Headers.AcceptRanges.Add("bytes");
        return response;
    });

    [Fact]
    public async Task RangeFromTo()
    {
        var handler = RangeServer();
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/file").Range(5, 9).Send();

        Assert.Equal("bytes=5-9", handler.Last.Headers["Range"]);
        Assert.True(res.IsPartialContent);
        Assert.Equal(5, res.ContentRange!.From);
        Assert.Equal(9, res.ContentRange.To);
        Assert.Equal(20, res.ContentRange.Length);
        Assert.Equal("56789", await res.Text());
    }

    [Fact]
    public async Task RangeFromToEnd()
    {
        var handler = RangeServer();
        using var client = TestClient.Create(handler);

        var text = await client.Get("/file").Range(15).Send().ErrorForStatus().Text();

        Assert.Equal("bytes=15-", handler.Last.Headers["Range"]);
        Assert.Equal("fghij", text);
    }

    [Fact]
    public async Task RangeSuffixReadsTheTail()
    {
        var handler = RangeServer();
        using var client = TestClient.Create(handler);

        var text = await client.Get("/log").RangeSuffix(3).Send().Text();

        Assert.Equal("bytes=-3", handler.Last.Headers["Range"]);
        Assert.Equal("hij", text);
    }

    [Fact]
    public async Task IfRangeMatchingETagGetsPartialContent()
    {
        using var client = TestClient.Create(RangeServer());

        using var res = await client.Get("/file").Range(10).IfRange(ResourceETag).Send();

        Assert.True(res.IsPartialContent);
    }

    [Fact]
    public async Task IfRangeChangedETagGetsWholeResource()
    {
        var handler = RangeServer();
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/file").Range(10).IfRange("old").Send();

        Assert.Equal("\"old\"", handler.Last.Headers["If-Range"]);
        Assert.False(res.IsPartialContent);
        Assert.Null(res.ContentRange);
        Assert.Equal(Resource, await res.Text());
    }

    [Fact]
    public async Task IfRangeDate()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").Range(0, 0).IfRange(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero)).Send();

        Assert.Equal("Thu, 08 Oct 2026 00:00:00 GMT", handler.Last.Headers["If-Range"]);
    }

    [Fact]
    public async Task UnsatisfiableRangeIsAStatusError()
    {
        using var client = TestClient.Create(RangeServer());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/file").Range(100).Send().ErrorForStatus());

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, ex.StatusCode);
    }

    [Theory]
    [InlineData(-1L, null)]
    [InlineData(10L, 5L)]
    public async Task InvalidRangeIsBuilderError(long from, long? to)
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Range(from, to).Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task InvalidSuffixIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").RangeSuffix(0).Send());

        Assert.True(ex.IsBuilder);
    }
}
