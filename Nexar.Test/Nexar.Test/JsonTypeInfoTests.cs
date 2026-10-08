using System.Net;
using System.Text.Json.Serialization;

namespace Nexar.Test;

public record Pet(int Id, string Name);

public record PetPage(List<Pet> Items);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Pet))]
[JsonSerializable(typeof(List<Pet>))]
[JsonSerializable(typeof(PetPage))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
public partial class TestJsonContext : JsonSerializerContext;

/// <summary>The trimming/AOT-safe overloads; samples/Nexar.AotSmoke checks them in a native binary.</summary>
public class JsonTypeInfoTests
{
    [Fact]
    public async Task JsonBodyAndResponse()
    {
        var handler = new FakeHandler(async (request, ct) =>
            FakeHandler.Respond(HttpStatusCode.OK, await request.Content!.ReadAsStringAsync(ct)));
        using var client = TestClient.Create(handler);

        var pet = await client.Post("/pets").Json(new Pet(1, "Rex"), TestJsonContext.Default.Pet).Send().Json(TestJsonContext.Default.Pet);

        Assert.Equal("{\"id\":1,\"name\":\"Rex\"}", handler.Last.Body);
        Assert.Equal(new Pet(1, "Rex"), pet);
    }

    [Fact]
    public async Task StreamingReaders()
    {
        using var lines = TestClient.Create(new FakeHandler(body: "{\"id\":1,\"name\":\"a\"}\n{\"id\":2,\"name\":\"b\"}\n"));
        using var array = TestClient.Create(new FakeHandler(body: "[{\"id\":3,\"name\":\"c\"}]"));
        var pets = new List<Pet>();

        await foreach (var pet in lines.Get("/").Send().JsonLines(TestJsonContext.Default.Pet))
        {
            pets.Add(pet);
        }
        await foreach (var pet in array.Get("/").Send().JsonStream(TestJsonContext.Default.Pet))
        {
            pets.Add(pet);
        }

        Assert.Equal(new[] { 1, 2, 3 }, pets.Select(p => p.Id));
    }

    [Fact]
    public async Task Pagination()
    {
        var handler = new FakeHandler(request =>
        {
            var second = request.RequestUri!.Query.Contains("page=2");
            var response = FakeHandler.Respond(HttpStatusCode.OK, second ? "{\"items\":[{\"id\":2,\"name\":\"b\"}]}" : "{\"items\":[{\"id\":1,\"name\":\"a\"}]}");
            if (!second)
            {
                response.Headers.TryAddWithoutValidation("Link", "</pets?page=2>; rel=\"next\"");
            }
            return response;
        });
        using var client = TestClient.Create(handler);
        var ids = new List<int>();

        await foreach (var pet in client.Get("/pets").Paginate(TestJsonContext.Default.PetPage, page => page.Items))
        {
            ids.Add(pet.Id);
        }

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public async Task ErrorBodyAndProblemExtensions()
    {
        using var client = TestClient.Create(new FakeHandler(HttpStatusCode.BadRequest,
            "{\"id\":0,\"name\":\"bad\",\"errors\":{\"Name\":[\"required\"]}}", "application/problem+json"));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().ErrorForStatus());

        Assert.Equal("bad", ex.Json(TestJsonContext.Default.Pet)!.Name);
        Assert.Equal("required", ex.Problem!.Extension("errors", TestJsonContext.Default.DictionaryStringStringArray)!["Name"][0]);
    }

    [Fact]
    public async Task KeyValueFormAndQueryNeedNoReflection()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/").Query(new Dictionary<string, string> { ["q"] = "x" }).Form(new[] { KeyValuePair.Create("a", "1") }).Send();

        Assert.Equal("?q=x", handler.Last.Url.Query);
        Assert.Equal("a=1", handler.Last.Body);
    }
}
