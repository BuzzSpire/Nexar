using System.Net;
using System.Text.Json;

namespace Nexar.Test;

public class ProblemDetailsTests
{
    private static async Task<NexarException> Fail(string body, string mediaType, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        using var client = TestClient.Create(new FakeHandler(status, body, mediaType));
        return await Assert.ThrowsAsync<NexarException>(() => client.Post("/users").Send().ErrorForStatus());
    }

    [Fact]
    public async Task ParsesStandardMembersAndExtensions()
    {
        var ex = await Fail("""
            {
              "type": "https://example.com/probs/validation",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "detail": "Name is required",
              "instance": "/users/requests/123",
              "traceId": "00-abc-01",
              "errors": { "Name": ["The Name field is required."] }
            }
            """, "application/problem+json");

        var problem = ex.Problem!;
        Assert.Equal("https://example.com/probs/validation", problem.Type);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Name is required", problem.Detail);
        Assert.Equal("/users/requests/123", problem.Instance);
        Assert.Equal("00-abc-01", problem.Extensions["traceId"].GetString());
        Assert.Equal("The Name field is required.", problem.Extension<Dictionary<string, string[]>>("errors")!["Name"][0]);
        Assert.Contains("One or more validation errors occurred.", ex.Message);
    }

    [Fact]
    public async Task TypeDefaultsToAboutBlank()
    {
        var ex = await Fail("{\"title\":\"Not Found\",\"status\":404}", "application/problem+json", HttpStatusCode.NotFound);

        Assert.Equal("about:blank", ex.Problem!.Type);
    }

    [Fact]
    public async Task MembersWithWrongTypesAreIgnored()
    {
        var ex = await Fail("{\"type\":42,\"title\":[\"x\"],\"status\":\"400\",\"detail\":\"ok\"}", "application/problem+json");

        Assert.Equal("about:blank", ex.Problem!.Type);
        Assert.Null(ex.Problem.Title);
        Assert.Null(ex.Problem.Status);
        Assert.Equal("ok", ex.Problem.Detail);
    }

    [Theory]
    [InlineData("{\"error\":\"bad\"}", "application/json")]
    [InlineData("not json", "application/problem+json")]
    [InlineData("[1,2]", "application/problem+json")]
    [InlineData("<problem/>", "application/problem+xml")]
    public async Task ProblemIsNullForOtherBodies(string body, string mediaType)
    {
        var ex = await Fail(body, mediaType);

        Assert.Null(ex.Problem);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task ExtensionWithWrongShapeIsDefault()
    {
        var ex = await Fail("{\"errors\":\"not an object\"}", "application/problem+json");

        Assert.Null(ex.Problem!.Extension<Dictionary<string, string[]>>("errors"));
        Assert.Null(ex.Problem.Extension<string>("missing"));
        Assert.Equal(JsonValueKind.String, ex.Problem.Extensions["errors"].ValueKind);
    }
}
