using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nexar.Test;

public class CharsetTests
{
    private const string Turkish = "Çığ düştü, şoför İğdır'a gitti";
    private const string Japanese = "こんにちは世界";

    static CharsetTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static FakeHandler Serve(byte[] body, string? contentType) => new(_ =>
    {
        var content = new ByteArrayContent(body);
        if (contentType != null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    });

    [Theory]
    [InlineData("iso-8859-9", Turkish)]
    [InlineData("windows-1254", Turkish)]
    [InlineData("shift_jis", Japanese)]
    public async Task TextUsesTheResponseCharset(string charset, string text)
    {
        var body = Encoding.GetEncoding(charset).GetBytes(text);
        using var client = TestClient.Create(Serve(body, $"text/plain; charset={charset}"));

        Assert.Equal(text, await client.Get("/").Send().Text());
    }

    [Fact]
    public async Task FallbackIsUsedWhenThereIsNoCharset()
    {
        var latin5 = Encoding.GetEncoding("iso-8859-9");
        using var client = TestClient.Create(Serve(latin5.GetBytes(Turkish), "text/plain"));

        Assert.Equal(Turkish, await client.Get("/").Send().Text(latin5));
    }

    [Fact]
    public async Task HeaderCharsetWinsOverFallback()
    {
        using var client = TestClient.Create(Serve(Encoding.UTF8.GetBytes(Turkish), "text/plain; charset=utf-8"));

        Assert.Equal(Turkish, await client.Get("/").Send().Text(Encoding.GetEncoding("iso-8859-9")));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public async Task ByteOrderMarkIsDetectedAndStripped(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var body = encoding.GetPreamble().Concat(encoding.GetBytes(Turkish)).ToArray();
        using var client = TestClient.Create(Serve(body, contentType: null));

        Assert.Equal(Turkish, await client.Get("/").Send().Text(Encoding.Latin1));
    }

    [Fact]
    public async Task Utf8BomIsStrippedWhenCharsetIsDeclared()
    {
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("abc")).ToArray();
        using var client = TestClient.Create(Serve(body, "text/plain; charset=utf-8"));

        Assert.Equal("abc", await client.Get("/").Send().Text());
    }

    [Fact]
    public async Task UnknownCharsetIsDecodeError()
    {
        using var client = TestClient.Create(Serve("abc"u8.ToArray(), "text/plain; charset=klingon-1"));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().Text());

        Assert.True(ex.IsDecode);
        Assert.Contains("klingon-1", ex.Message);
    }

    [Fact]
    public async Task BodyWithEncodingSetsCharsetAndBytes()
    {
        byte[]? received = null;
        var handler = new FakeHandler(async (request, ct) =>
        {
            received = await request.Content!.ReadAsByteArrayAsync(ct);
            return FakeHandler.Respond(HttpStatusCode.OK);
        });
        using var client = TestClient.Create(handler);
        var latin5 = Encoding.GetEncoding("iso-8859-9");

        await client.Post("/").Body(Turkish, latin5, "application/xml").Send();

        Assert.Equal("application/xml; charset=iso-8859-9", handler.Last.ContentHeaders["Content-Type"]);
        Assert.Equal(latin5.GetBytes(Turkish), received);
    }

    [Fact]
    public async Task BodyHonorsCharsetInContentType()
    {
        byte[]? received = null;
        var handler = new FakeHandler(async (request, ct) =>
        {
            received = await request.Content!.ReadAsByteArrayAsync(ct);
            return FakeHandler.Respond(HttpStatusCode.OK);
        });
        using var client = TestClient.Create(handler);

        await client.Post("/").Body(Turkish, "text/plain; charset=windows-1254").Send();

        Assert.Equal(Encoding.GetEncoding("windows-1254").GetBytes(Turkish), received);
    }

    [Fact]
    public async Task BodyWithUnknownCharsetIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Post("/").Body("x", "text/plain; charset=klingon-1").Send());

        Assert.True(ex.IsBuilder);
    }
}
