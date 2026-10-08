namespace Nexar.Test;

public class HeaderValidationTests
{
    [Theory]
    [InlineData("X-Test", "a\r\nInjected: 1")]
    [InlineData("X-Test", "a\nb")]
    [InlineData("X-Test", "a\0b")]
    [InlineData("X Test", "value")]
    [InlineData("X-Test:", "value")]
    [InlineData("", "value")]
    [InlineData("X-\r\nTest", "value")]
    public async Task InvalidRequestHeaderIsBuilderError(string name, string value)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Header(name, value).Send());

        Assert.True(ex.IsBuilder);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ErrorMessageNeverContainsTheValue()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/").Header("X-Secret", "s3cr3t\r\nX-Evil: 1").Send());

        Assert.Contains("X-Secret", ex.Message);
        Assert.DoesNotContain("s3cr3t", ex.Message);
        Assert.DoesNotContain("X-Evil", ex.Message);
    }

    [Theory]
    [InlineData("X-Custom_Header.v2", "plain value")]
    [InlineData("X-Tab", "a\tb")]
    [InlineData("X-Unicode", "Gökyüzü")]
    [InlineData("!#$%&'*+-.^_`|~", "symbols")]
    public async Task ValidHeadersAreSent(string name, string value)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").Header(name, value).Send();

        Assert.Equal(value, handler.Last.Headers[name]);
    }

    [Fact]
    public void InvalidDefaultHeaderIsBuilderError()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .DefaultHeader("X-Test", "a\r\nb")
            .Build());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public void InvalidApiKeyIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Auth.ApiKeyHeader("X-Api-Key", "key\r\nX-Evil: 1"));
        Assert.Throws<ArgumentException>(() => Auth.Bearer("token\nX-Evil: 1"));
    }

    [Fact]
    public async Task InvalidTokenFromProviderIsAuthError()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer(_ => ValueTask.FromResult("t\r\nX-Evil: 1"))));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsAuth);
        Assert.Empty(handler.Requests);
    }
}
