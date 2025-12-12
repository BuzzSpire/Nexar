using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System.Net;
using Xunit.Abstractions;
using JsonSerializer = System.Text.Json.JsonSerializer;

/// <summary>
/// This class contains unit tests for the Nexar class.
/// </summary>
public class NexarTests
{
    private readonly ITestOutputHelper _testOutputHelper;

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
    public NexarTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _client = new HttpClient(_mockHttpMessageHandler.Object);
        _nexar = new Nexar.Nexar();

        // Setup the mock HttpMessageHandler to return a successful HTTP response.
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Successful response")
        };
        _mockHttpMessageHandler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(httpResponse);

    }

    /// <summary>
    /// Test case for the GetAsync method of the Nexar class.
    /// This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReturnsSuccessfulResponse()
    {
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
        // Call the GetAsync method and verify that it throws an exception.
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };
        await Assert.ThrowsAsync<HttpRequestException>(() => _nexar.GetAsync("https://api.example.com", headers));
    }

    /// <summary>
    ///  Test case for the PostAsync method of the Nexar class.
    ///  This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task PostAsync_ReturnsSuccessfulResponse()
    {
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };

        var body = new
        {
            id = 21,
            title = "test product",
            price = 13.5,
            description = "lorem ipsum set",
            image = "https://i.pravatar.cc",
            category = "electronic"
        };

        var result =
            await _nexar.PostAsync("https://fakestoreapi.com/products", headers, body);

        Assert.Equal(JsonSerializer.Serialize(body), result);
    }

    /// <summary>
    ///  Test case for the PutAsync method of the Nexar class.
    ///  This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task PutAsync_ReturnsSuccessfulResponse()
    {
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };

        var body = new
        {
            id = 21,
            title = "test product",
            price = 13.5,
            description = "lorem ipsum set",
            image = "https://i.pravatar.cc",
            category = "electronic"
        };

        var result =
            await _nexar.PutAsync("https://fakestoreapi.com/products/21", headers, body);

        Assert.Equal(JsonSerializer.Serialize(body), result);
    }

    /// <summary>
    ///  Test case for the DeleteAsync method of the Nexar class.
    ///  This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ReturnsSuccessfulResponse()
    {
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };

        var result = _nexar.DeleteAsync("https://fakestoreapi.com/products/6", headers);

        var expected = "{\"id\":6,\"title\":\"Solid Gold Petite Micropave \",\"price\":168,\"description\":\"Satisfaction Guaranteed. Return or exchange any order within 30 days.Designed and sold by Hafeez Center in the United States. Satisfaction Guaranteed. Return or exchange any order within 30 days.\",\"category\":\"jewelery\",\"image\":\"https://fakestoreapi.com/img/61sbMiUnoGL._AC_UL640_QL65_ML3_.jpg\",\"rating\":{\"rate\":3.9,\"count\":70}}";
        var actual = await result;
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///  Test case for the PatchAsync method of the Nexar class.
    ///  This test verifies that the method returns a successful response.
    /// </summary>
    [Fact]
    public async Task PatchAsync_ReturnsSuccessfulResponse()
    {
        var headers = new Dictionary<string, string> { { "Accept", "application/json" } };

        var body = new
        {
            id = 21,
            title = "test product",
            price = 13.5,
            description = "lorem ipsum set",
            image = "https://i.pravatar.cc",
            category = "electronic"
        };

        var result =
            await _nexar.PatchAsync("https://fakestoreapi.com/products/21", headers, body);

        Assert.Equal(JsonSerializer.Serialize(body), result);
    }
}
