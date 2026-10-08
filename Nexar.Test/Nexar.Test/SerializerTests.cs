using System.Net;
using System.Text;
using System.Text.Json;
using Nexar.Testing;

namespace Nexar.Test;

public class Invoice
{
    public int Number { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public class SerializerTests
{
    static SerializerTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static HttpResponseMessage Xml(string xml, string contentType) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.GetEncoding(System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType).CharSet ?? "utf-8").GetBytes(xml))
        {
            Headers = { ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType) }
        }
    };

    [Fact]
    public async Task XmlRoundTrip()
    {
        var mock = new MockHttp();
        mock.OnPost("/invoices").RespondWith(r => Xml(r.Body!, "application/xml; charset=utf-8"));
        using var client = mock.CreateClient();

        var sent = new Invoice { Number = 7, Customer = "Çağrı Öztürk", Total = 12.5m };
        var echoed = await client.Post("/invoices").Body(sent, XmlContentSerializer.Default).Send().As<Invoice>(XmlContentSerializer.Default);

        Assert.StartsWith("application/xml; charset=utf-8", mock.Requests[0].Header("Content-Type"));
        Assert.Contains("<Customer>Çağrı Öztürk</Customer>", mock.Requests[0].Body);
        Assert.Equal((7, "Çağrı Öztürk", 12.5m), (echoed.Number, echoed.Customer, echoed.Total));
    }

    [Fact]
    public async Task XmlHonorsTheResponseCharset()
    {
        var mock = new MockHttp();
        mock.OnGet("/").RespondWith(_ => Xml("<Invoice><Number>1</Number><Customer>Şükrü Ağa</Customer><Total>3</Total></Invoice>", "text/xml; charset=windows-1254"));
        using var client = mock.CreateClient();

        var invoice = await client.Get("/").Send().As<Invoice>();   // picked by Content-Type

        Assert.Equal("Şükrü Ağa", invoice.Customer);
    }

    [Fact]
    public async Task XmlWithCustomEncoding()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient();

        await client.Post("/").Body(new Invoice { Customer = "Gül" }, new XmlContentSerializer(Encoding.GetEncoding("iso-8859-9"))).Send();

        Assert.Equal("application/xml; charset=iso-8859-9", mock.Requests[0].Header("Content-Type"));
    }

    [Fact]
    public async Task InvalidXmlIsDecodeError()
    {
        var mock = new MockHttp();
        mock.OnGet("/").RespondWith(_ => Xml("<Invoice><Number>not a number</Number></Invoice>", "application/xml"));
        using var client = mock.CreateClient();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().As<Invoice>());

        Assert.True(ex.IsDecode);
    }

    /// <summary>A stand-in for a third-party format, e.g. Newtonsoft or MessagePack.</summary>
    private sealed class UpperCaseJson : IContentSerializer
    {
        public string MediaType => "application/vnd.upper+json";

        public HttpContent Serialize<T>(T value) =>
            new StringContent(JsonSerializer.Serialize(value).ToUpperInvariant(), Encoding.UTF8, MediaType);

        public async ValueTask<T?> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken) =>
            JsonSerializer.Deserialize<T>((await content.ReadAsStringAsync(cancellationToken)).ToLowerInvariant(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    [Fact]
    public async Task RegisteredSerializerIsUsedForBothDirections()
    {
        var mock = new MockHttp();
        mock.OnPost("/").RespondWith(r => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(r.Body!, Encoding.UTF8, "application/vnd.upper+json")
        });
        using var client = mock.CreateClient(configure: b => b.Serializer(new UpperCaseJson()));

        var result = await client.Post("/").Serialized(new Invoice { Customer = "ada" }).Send().As<Invoice>();

        Assert.Contains("\"CUSTOMER\":\"ADA\"", mock.Requests[0].Body);
        Assert.Equal("ada", result.Customer);
    }

    [Fact]
    public async Task SerializedFallsBackToJson()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient();

        await client.Post("/").Serialized(new Invoice { Number = 1 }).Send();

        Assert.StartsWith("application/json", mock.Requests[0].Header("Content-Type"));
    }

    [Fact]
    public async Task AsUsesJsonForJsonResponses()
    {
        var mock = new MockHttp();
        mock.OnGet("/").RespondJson(new Invoice { Number = 9 });
        using var client = mock.CreateClient();

        var invoice = await client.Get("/").Send().As<Invoice>();

        Assert.Equal(9, invoice.Number);
    }

    [Fact]
    public async Task UnknownContentTypeIsDecodeError()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.OK, "a,b", "text/csv");
        using var client = mock.CreateClient();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().As<Invoice>());

        Assert.True(ex.IsDecode);
        Assert.Contains("text/csv", ex.Message);
    }

    private sealed class Failing : IContentSerializer
    {
        public string MediaType => "application/x-failing";
        public HttpContent Serialize<T>(T value) => throw new NotSupportedException("cannot write");
        public ValueTask<T?> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task SerializationFailureIsBuilderError()
    {
        using var client = new MockHttp().CreateClient();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Post("/").Body(new Invoice(), new Failing()).Send());

        Assert.True(ex.IsBuilder);
        Assert.Contains("cannot write", ex.Message);
    }

    [Fact]
    public async Task DerivedClientsKeepSerializers()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient(configure: b => b.Serializer(XmlContentSerializer.Default));
        using var derived = client.With(_ => { });

        await derived.Post("/").Serialized(new Invoice()).Send();

        Assert.StartsWith("application/xml", mock.Requests[0].Header("Content-Type"));
    }
}
