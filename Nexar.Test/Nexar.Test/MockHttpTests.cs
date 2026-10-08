using System.Net;
using Nexar.Testing;

namespace Nexar.Test;

public class MockHttpTests
{
    private record User(int Id, string Name);

    [Fact]
    public async Task MatchesMethodAndPath()
    {
        var mock = new MockHttp();
        mock.OnGet("/users/1").RespondJson(new User(1, "Ada"));
        mock.OnDelete("/users/1").Respond(HttpStatusCode.NoContent);
        using var client = mock.CreateClient();

        var user = await client.Get("/users/1").Send().Json<User>();
        using var deleted = await client.Delete("/users/1").Send();

        Assert.Equal(new User(1, "Ada"), user);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task MatchesQueryHeadersAndJsonBody()
    {
        var mock = new MockHttp();
        mock.OnPost("/users")
            .WithQuery("dry", "true")
            .WithHeader("X-Tenant", "acme")
            .WithJsonBody<User>(u => u.Name == "Ada")
            .Respond(HttpStatusCode.Created);
        mock.OnPost("/users").Respond(HttpStatusCode.BadRequest);
        using var client = mock.CreateClient();

        using var created = await client.Post("/users").Query("dry", true).Header("X-Tenant", "acme").Json(new User(0, "Ada")).Send();
        using var rejected = await client.Post("/users").Json(new User(0, "Bob")).Send();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task WildcardPathAndCustomResponse()
    {
        var mock = new MockHttp();
        mock.OnGet("/files/*").RespondWith(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.Url.AbsolutePath) });
        using var client = mock.CreateClient();

        Assert.Equal("/files/a/b.txt", await client.Get("/files/a/b.txt").Send().Text());
    }

    [Fact]
    public async Task SequencesRepeatTheLastAnswer()
    {
        var mock = new MockHttp();
        mock.OnGet("/flaky").Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, "ok");
        using var client = mock.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var res = await client.Get("/flaky").Send();
            statuses.Add(res.StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.OK }, statuses);
    }

    [Fact]
    public async Task ThrowsAndDelaysSimulateFailures()
    {
        var mock = new MockHttp();
        mock.OnGet("/down").Throws(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        mock.OnGet("/slow").Delay(TimeSpan.FromSeconds(30)).Respond();
        using var client = mock.CreateClient(configure: b => b.Timeout(TimeSpan.FromMilliseconds(50)));

        var down = await Assert.ThrowsAsync<NexarException>(() => client.Get("/down").Send());
        var slow = await Assert.ThrowsAsync<NexarException>(() => client.Get("/slow").Send());

        Assert.True(down.IsConnect);
        Assert.True(slow.IsTimeout);
    }

    [Fact]
    public async Task UnmatchedRequestListsTheRoutes()
    {
        var mock = new MockHttp();
        mock.OnGet("/users").WithQuery("page", "1").Respond();
        using var client = mock.CreateClient();

        var ex = await Assert.ThrowsAsync<MockHttpException>(() => client.Get("/users").Query("page", 2).Send());

        Assert.Contains("No route matched GET https://mock.test/users?page=2", ex.Message);
        Assert.Contains("GET /users [?page=1]", ex.Message);
    }

    [Fact]
    public async Task VerifyAllCalledReportsMissingAndMiscountedRoutes()
    {
        var mock = new MockHttp();
        mock.OnGet("/called").Times(2).Respond();
        mock.OnGet("/never").Respond();
        using var client = mock.CreateClient();

        using var res = await client.Get("/called").Send();
        var ex = Assert.Throws<MockHttpException>(mock.VerifyAllCalled);

        Assert.Contains("GET /called was called 1 time(s), expected 2", ex.Message);
        Assert.Contains("GET /never was never called", ex.Message);
    }

    [Fact]
    public async Task RecordsRequests()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient();

        await client.Put("/a").BearerAuth("t").Json(new User(1, "x")).Send();

        var request = Assert.Single(mock.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("Bearer t", request.Header("Authorization"));
        Assert.Equal(new User(1, "x"), request.Json<User>());
    }
}
