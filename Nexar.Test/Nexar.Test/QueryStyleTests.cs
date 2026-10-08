namespace Nexar.Test;

public class QueryStyleTests
{
    private static async Task<string> QueryOf(object values, QueryStyle? style = null, Action<ClientBuilder>? configure = null)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, configure);
        var request = client.Get("/items");
        await (style is { } s ? request.Query(values, s) : request.Query(values)).Send();
        return Uri.UnescapeDataString(handler.Last.Url.Query.TrimStart('?'));
    }

    [Theory]
    [InlineData(ArrayStyle.Repeat, "ids=1&ids=2&ids=3")]
    [InlineData(ArrayStyle.Brackets, "ids[]=1&ids[]=2&ids[]=3")]
    [InlineData(ArrayStyle.Comma, "ids=1,2,3")]
    [InlineData(ArrayStyle.Index, "ids[0]=1&ids[1]=2&ids[2]=3")]
    public async Task ArrayStyles(ArrayStyle arrays, string expected)
    {
        Assert.Equal(expected, await QueryOf(new { ids = new[] { 1, 2, 3 } }, new QueryStyle(arrays)));
    }

    [Theory]
    [InlineData(NestedStyle.Brackets, "filter[status]=open&filter[owner][id]=7")]
    [InlineData(NestedStyle.Dot, "filter.status=open&filter.owner.id=7")]
    public async Task NestedStyles(NestedStyle nested, string expected)
    {
        var values = new { filter = new { status = "open", owner = new { id = 7 } } };

        Assert.Equal(expected, await QueryOf(values, new QueryStyle(Nested: nested)));
    }

    [Fact]
    public async Task ArraysOfObjectsWithIndexAndBrackets()
    {
        var values = new { items = new[] { new { name = "a", qty = 1 }, new { name = "b", qty = 2 } } };

        var query = await QueryOf(values, new QueryStyle(ArrayStyle.Index, NestedStyle.Brackets));

        Assert.Equal("items[0][name]=a&items[0][qty]=1&items[1][name]=b&items[1][qty]=2", query);
    }

    [Fact]
    public async Task EmptyArraysAndNullsAreSkipped()
    {
        var values = new { ids = Array.Empty<int>(), tags = new string?[] { "x", null, "y" }, name = (string?)null };

        Assert.Equal("tags=x,y", await QueryOf(values, new QueryStyle(ArrayStyle.Comma)));
        Assert.Equal("tags=x&tags=y", await QueryOf(values));
    }

    [Fact]
    public async Task ClientDefaultStyleAppliesToQueryAndForm()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.QueryStyle(ArrayStyle.Brackets, NestedStyle.Brackets));

        await client.Post("/orders")
            .Query(new { tags = new[] { "a", "b" } })
            .Form(new { order = new { id = 5, lines = new[] { 1, 2 } } })
            .Send();

        Assert.Equal("tags[]=a&tags[]=b", Uri.UnescapeDataString(handler.Last.Url.Query.TrimStart('?')));
        Assert.Equal("order[id]=5&order[lines][]=1&order[lines][]=2", Uri.UnescapeDataString(handler.Last.Body!));
    }

    [Fact]
    public async Task PerCallStyleOverridesClientDefault()
    {
        var query = await QueryOf(new { ids = new[] { 1, 2 } }, new QueryStyle(ArrayStyle.Comma),
            b => b.QueryStyle(ArrayStyle.Brackets));

        Assert.Equal("ids=1,2", query);
    }

    [Fact]
    public async Task DictionariesWithArrays()
    {
        var values = new Dictionary<string, object?> { ["ids"] = new[] { 1, 2 }, ["q"] = "x y", ["skip"] = null };

        Assert.Equal("ids[]=1&ids[]=2&q=x y", await QueryOf(values, new QueryStyle(ArrayStyle.Brackets)));
    }

    [Fact]
    public async Task NestedObjectsAreRejectedByDefault()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Query(new { filter = new { a = 1 } }).Send());

        Assert.True(ex.IsBuilder);
        Assert.Contains("NestedStyle", ex.Message);
    }

    [Theory]
    [InlineData(ArrayStyle.Comma)]
    [InlineData(ArrayStyle.Repeat)]
    public async Task ObjectsInArraysNeedIndexOrBrackets(ArrayStyle arrays)
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/").Query(new { items = new[] { new { a = 1 } } }, new QueryStyle(arrays, NestedStyle.Brackets)).Send());

        Assert.True(ex.IsBuilder);
    }
}
