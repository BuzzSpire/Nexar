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
        var result = await _nexar.GetAsync("https://fakestoreapi.com/products/1", headers);

        Assert.Equal("{\"id\":1,\"title\":\"Fjallraven - Foldsack No. 1 Backpack, Fits 15 Laptops\",\"price\":109.95,\"description\":\"Your perfect pack for everyday use and walks in the forest. Stash your laptop (up to 15 inches) in the padded sleeve, your everyday\",\"category\":\"men's clothing\",\"image\":\"https://fakestoreapi.com/img/81fPKd-2AYL._AC_SL1500_.jpg\",\"rating\":{\"rate\":3.9,\"count\":120}}", result);
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