using Moq;
using System.Net;
using Moq.Protected;

/// <summary>
/// This class contains unit tests for the Nexar class.
/// </summary>
public class NexarTests
{
    /// <summary>
    /// Mock object for HttpMessageHandler to simulate HTTP requests.
    /// </summary>
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;

    /// <summary>
    /// HttpClient object that uses the mocked HttpMessageHandler.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Instance of the Nexar class that is being tested.
    /// </summary>
    private readonly Nexar.Nexar _nexar;

    /// <summary>
    /// Constructor for the NexarTests class.
    /// Initializes the mock HttpMessageHandler, HttpClient, and Nexar objects.
    /// </summary>
    public NexarTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _client = new HttpClient(_mockHttpMessageHandler.Object);
        _nexar = new Nexar.Nexar();
    }

    /// <summary>
    /// Test case for the GetAsync method of the Nexar class.
    /// This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReturnsSuccessfulResponse()
    {
        // Setup the mock HttpMessageHandler to return a successful HTTP response.
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Successful response")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Call the GetAsync method and verify the response.
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };
        var result = await _nexar.GetAsync("https://freetestapi.com/api/v1/authors/1", headers);

        Assert.Equal("{\"id\":1,\"name\":\"William Shakespeare\",\"birth_year\":1564,\"death_year\":1616,\"nationality\":\"English\",\"genre\":[\"Drama\",\"Poetry\"],\"notable_works\":[\"Hamlet\",\"Romeo and Juliet\",\"Macbeth\",\"Othello\",\"King Lear\"],\"award\":\"None\",\"biography\":\"William Shakespeare was an English playwright, poet, and actor, widely regarded as the greatest writer in the English language and the world's greatest dramatist.\",\"image\":\"https://upload.wikimedia.org/wikipedia/commons/thumb/5/5e/William_Shakespeare_by_John_Taylor.jpg/800px-William_Shakespeare_by_John_Taylor.jpg\"}", result);
    }

    /// <summary>
    /// Test case for the GetAsync method of the Nexar class.
    /// This test verifies that the method throws an exception for an unsuccessful response.
    /// </summary>
    [Fact]
    public async Task GetAsync_ThrowsExceptionForUnsuccessfulResponse()
    {
        // Setup the mock HttpMessageHandler to return a bad request HTTP response.
        var httpResponse = new HttpResponseMessage(HttpStatusCode.BadRequest);

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Call the GetAsync method and verify that it throws an exception.
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };
        await Assert.ThrowsAsync<HttpRequestException>(() => _nexar.GetAsync("https://api.example.com", headers));
    }
}