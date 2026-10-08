using System.Net;
using System.Text;

namespace Nexar.Test;

public class StreamingTests
{
    private static NexarClient Serving(string body, string mediaType = "text/event-stream") =>
        TestClient.Create(new FakeHandler(HttpStatusCode.OK, body, mediaType));

    private static async Task<List<ServerSentEvent>> EventsOf(string stream)
    {
        using var client = Serving(stream);
        var events = new List<ServerSentEvent>();
        await foreach (var e in client.Get("/events").Send().Events())
        {
            events.Add(e);
        }
        return events;
    }

    // ---- #65 Server-Sent Events: examples from the WHATWG HTML spec, section 9.2.6 ----

    [Fact]
    public async Task MultiLineData()
    {
        var events = await EventsOf("data: YHOO\ndata: +2\ndata: 10\n\n");

        var e = Assert.Single(events);
        Assert.Equal("message", e.Event);
        Assert.Equal("YHOO\n+2\n10", e.Data);
    }

    [Fact]
    public async Task CommentsIdsAndLeadingSpaces()
    {
        var events = await EventsOf(": test stream\n\ndata: first event\nid: 1\n\ndata:second event\nid\n\ndata:  third event\n\n");

        Assert.Equal(new[] { "first event", "second event", " third event" }, events.Select(e => e.Data));
        Assert.Equal(new[] { "1", "", "" }, events.Select(e => e.Id));
    }

    [Fact]
    public async Task EmptyDataAndIncompleteFinalEvent()
    {
        var events = await EventsOf("data\n\ndata\ndata\n\ndata:");

        Assert.Equal(new[] { "", "\n" }, events.Select(e => e.Data));
    }

    [Fact]
    public async Task SpaceAfterColonIsOptional()
    {
        var events = await EventsOf("data:test\n\ndata: test\n\n");

        Assert.Equal(new[] { "test", "test" }, events.Select(e => e.Data));
    }

    [Fact]
    public async Task EventTypeIdCarryOverAndRetry()
    {
        var events = await EventsOf("event: delta\nid: 42\nretry: 3000\ndata: {\"t\":\"Hel\"}\n\ndata: {\"t\":\"lo\"}\n\nevent: done\ndata: [DONE]\n\n");

        Assert.Equal(new[] { "delta", "message", "done" }, events.Select(e => e.Event));
        Assert.Equal(new[] { "42", "42", "42" }, events.Select(e => e.Id));
        Assert.Equal(TimeSpan.FromSeconds(3), events[0].Retry);
        Assert.Null(events[1].Retry);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task OtherLineEndings(string newline)
    {
        var events = await EventsOf($"data: a{newline}data: b{newline}{newline}data: c{newline}{newline}");

        Assert.Equal(new[] { "a\nb", "c" }, events.Select(e => e.Data));
    }

    [Fact]
    public async Task EventsWithoutDataAreDropped()
    {
        var events = await EventsOf("event: ping\n\nretry: 10\n\ndata: real\n\n");

        Assert.Equal("real", Assert.Single(events).Data);
    }

    [Fact]
    public async Task InvalidRetryAndNulInIdAreIgnored()
    {
        var events = await EventsOf("id: ok\n\nretry: 10s\nid: bad\0id\ndata: x\n\n");

        var e = Assert.Single(events);
        Assert.Null(e.Retry);
        Assert.Equal("ok", e.Id);
    }

    // ---- #65 NDJSON -----------------------------------------------------------------

    private record LogEntry(string Level, string Message);

    [Fact]
    public async Task JsonLines()
    {
        using var client = Serving("{\"level\":\"info\",\"message\":\"a\"}\n\n{\"level\":\"warn\",\"message\":\"b\"}\r\n", "application/x-ndjson");
        var entries = new List<LogEntry>();

        await foreach (var entry in client.Get("/logs").Send().JsonLines<LogEntry>())
        {
            entries.Add(entry);
        }

        Assert.Equal(new[] { new LogEntry("info", "a"), new LogEntry("warn", "b") }, entries);
    }

    [Fact]
    public async Task InvalidJsonLineIsDecodeErrorWithLineNumber()
    {
        using var client = Serving("{\"level\":\"info\",\"message\":\"a\"}\nnot json\n", "application/x-ndjson");

        var ex = await Assert.ThrowsAsync<NexarException>(async () =>
        {
            await foreach (var _ in client.Get("/logs").Send().JsonLines<LogEntry>())
            {
            }
        });

        Assert.True(ex.IsDecode);
        Assert.Contains("line 2", ex.Message);
    }

    // ---- #66 JSON array streaming ------------------------------------------------------

    private record Order(int Id, decimal Total);

    [Fact]
    public async Task JsonStreamYieldsElements()
    {
        var json = "[" + string.Join(",", Enumerable.Range(1, 10_000).Select(i => $"{{\"id\":{i},\"total\":{i}.5}}")) + "]";
        using var client = Serving(json, "application/json");
        var count = 0;
        var sum = 0m;

        await foreach (var order in client.Get("/orders/export").Send().JsonStream<Order>())
        {
            count++;
            sum += order.Total;
        }

        Assert.Equal(10_000, count);
        Assert.Equal(Enumerable.Range(1, 10_000).Sum(i => i + 0.5m), sum);
    }

    [Fact]
    public async Task JsonStreamStopsEarlyWhenTheConsumerBreaks()
    {
        using var client = Serving("[{\"id\":1,\"total\":1},{\"id\":2,\"total\":2},{\"id\":3,\"total\":3}]", "application/json");
        var first = 0;

        await foreach (var order in client.Get("/").Send().JsonStream<Order>())
        {
            first = order.Id;
            break;
        }

        Assert.Equal(1, first);
    }

    [Fact]
    public async Task JsonStreamDecodeErrorNamesTheElement()
    {
        using var client = Serving("[{\"id\":1,\"total\":1},{\"id\":\"x\"}]", "application/json");

        var ex = await Assert.ThrowsAsync<NexarException>(async () =>
        {
            await foreach (var _ in client.Get("/").Send().JsonStream<Order>())
            {
            }
        });

        Assert.True(ex.IsDecode);
    }

    // ---- #85 Link header and pagination ----------------------------------------------

    [Fact]
    public async Task LinksAreParsedAndResolved()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.OK, "[]");
            response.Headers.TryAddWithoutValidation("Link",
                "<https://api.test/items?page=2>; rel=\"next\", </items?page=9>; rel=\"last\"; title=\"a, b\", <https://docs.test>; rel=\"help describedby\"");
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/items").Send();

        Assert.Equal("https://api.test/items?page=2", res.Links["next"].AbsoluteUri);
        Assert.Equal("https://api.test/items?page=9", res.Links["last"].AbsoluteUri);
        Assert.Equal("https://docs.test/", res.Links["help"].AbsoluteUri);
        Assert.Equal("https://docs.test/", res.Links["DescribedBy"].AbsoluteUri);
        Assert.False(res.Links.ContainsKey("prev"));
    }

    [Fact]
    public async Task NoLinkHeaderMeansNoLinks()
    {
        using var client = TestClient.Create(new FakeHandler());

        using var res = await client.Get("/").Send();

        Assert.Empty(res.Links);
    }

    /// <summary>Three pages of numbers; each links to the next with a relative URL.</summary>
    private static FakeHandler Pages(Func<int, string>? body = null) => new(request =>
    {
        var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"] ?? "1");
        var response = FakeHandler.Respond(HttpStatusCode.OK,
            body?.Invoke(page) ?? $"[{page * 10 + 1},{page * 10 + 2}]");
        if (page < 3)
        {
            response.Headers.TryAddWithoutValidation("Link", $"</repos?page={page + 1}>; rel=\"next\"");
        }
        return response;
    });

    [Fact]
    public async Task PaginateFollowsNextLinks()
    {
        var handler = Pages();
        using var client = TestClient.Create(handler);
        var items = new List<int>();

        await foreach (var item in client.Get("/repos").BearerAuth("t").Paginate<int>())
        {
            items.Add(item);
        }

        Assert.Equal(new[] { 11, 12, 21, 22, 31, 32 }, items);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer t", r.Headers["Authorization"]));
    }

    private record Page(List<int> Items);

    [Fact]
    public async Task PaginateWithWrappedItems()
    {
        using var client = TestClient.Create(Pages(page => $"{{\"items\":[{page}]}}"));
        var items = new List<int>();

        await foreach (var item in client.Get("/repos").Paginate<Page, int>(p => p.Items))
        {
            items.Add(item);
        }

        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public async Task PaginateStopsEarlyWithoutFetchingMore()
    {
        var handler = Pages();
        using var client = TestClient.Create(handler);

        await foreach (var item in client.Get("/repos").Paginate<int>())
        {
            if (item == 12)
            {
                break;
            }
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PaginateStopsOnALinkLoop()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.OK, "[1]");
            response.Headers.TryAddWithoutValidation("Link", "</repos>; rel=\"next\"");
            return response;
        });
        using var client = TestClient.Create(handler);
        var count = 0;

        await foreach (var _ in client.Get("/repos").Paginate<int>())
        {
            count++;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PaginateErrorStops()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            if (++calls == 2)
            {
                return FakeHandler.Respond(HttpStatusCode.InternalServerError);
            }
            var response = FakeHandler.Respond(HttpStatusCode.OK, "[1]");
            response.Headers.TryAddWithoutValidation("Link", "</repos?page=2>; rel=\"next\"");
            return response;
        });
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(async () =>
        {
            await foreach (var _ in client.Get("/repos").Paginate<int>())
            {
            }
        });

        Assert.True(ex.IsStatus);
    }
}
