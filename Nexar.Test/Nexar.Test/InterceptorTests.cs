using Nexar.Configuration;
using Nexar.Interceptors;

namespace Nexar.Test;

/// <summary>
/// Unit tests for Interceptor functionality
/// </summary>
public class InterceptorTests
{
    [Fact]
    public void InterceptorCollection_Add_AddsInterceptor()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor = new TestInterceptor();

        // Act
        collection.Add(interceptor);

        // Assert
        Assert.Single(collection);
        Assert.Contains(interceptor, collection);
    }

    [Fact]
    public void InterceptorCollection_Remove_RemovesInterceptor()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor = new TestInterceptor();
        collection.Add(interceptor);

        // Act
        var result = collection.Remove(interceptor);

        // Assert
        Assert.True(result);
        Assert.Empty(collection);
    }

    [Fact]
    public void InterceptorCollection_Clear_RemovesAllInterceptors()
    {
        // Arrange
        var collection = new InterceptorCollection();
        collection.Add(new TestInterceptor());
        collection.Add(new TestInterceptor());
        collection.Add(new TestInterceptor());

        // Act
        collection.Clear();

        // Assert
        Assert.Empty(collection);
    }

    [Fact]
    public void InterceptorCollection_Count_ReturnsCorrectCount()
    {
        // Arrange
        var collection = new InterceptorCollection();

        // Act
        collection.Add(new TestInterceptor());
        collection.Add(new TestInterceptor());

        // Assert
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public async Task OnRequestAsync_CallsAllInterceptors()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestInterceptor();
        collection.Add(interceptor1);
        collection.Add(interceptor2);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com");

        // Act
        await collection.OnRequestAsync(request);

        // Assert
        Assert.True(interceptor1.OnRequestCalled);
        Assert.True(interceptor2.OnRequestCalled);
    }

    [Fact]
    public async Task OnResponseAsync_CallsAllInterceptors()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestInterceptor();
        collection.Add(interceptor1);
        collection.Add(interceptor2);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        // Act
        await collection.OnResponseAsync(response);

        // Assert
        Assert.True(interceptor1.OnResponseCalled);
        Assert.True(interceptor2.OnResponseCalled);
    }

    [Fact]
    public async Task OnErrorAsync_CallsAllInterceptors()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestInterceptor();
        collection.Add(interceptor1);
        collection.Add(interceptor2);

        var exception = new Exception("Test error");

        // Act
        await collection.OnErrorAsync(exception);

        // Assert
        Assert.True(interceptor1.OnErrorCalled);
        Assert.True(interceptor2.OnErrorCalled);
    }

    [Fact]
    public void InterceptorCollection_IsEnumerable()
    {
        // Arrange
        var collection = new InterceptorCollection();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestInterceptor();
        collection.Add(interceptor1);
        collection.Add(interceptor2);

        // Act
        var list = collection.ToList();

        // Assert
        Assert.Equal(2, list.Count);
        Assert.Contains(interceptor1, list);
        Assert.Contains(interceptor2, list);
    }

    [Fact]
    public void Nexar_WithInterceptors_CanAddInterceptors()
    {
        // Arrange
        var config = new NexarConfig
        {
            BaseUrl = "https://api.example.com"
        };
        var nexar = new global::Nexar.Nexar(config);
        var interceptor = new TestInterceptor();

        // Act
        nexar.Interceptors.Add(interceptor);

        // Assert
        Assert.Single(nexar.Interceptors);
        Assert.Contains(interceptor, nexar.Interceptors);
    }

    // Test interceptor implementation
    private class TestInterceptor : IInterceptor
    {
        public bool OnRequestCalled { get; private set; }
        public bool OnResponseCalled { get; private set; }
        public bool OnErrorCalled { get; private set; }

        public Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request)
        {
            OnRequestCalled = true;
            return Task.FromResult(request);
        }

        public Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response)
        {
            OnResponseCalled = true;
            return Task.FromResult(response);
        }

        public Task OnErrorAsync(Exception exception)
        {
            OnErrorCalled = true;
            return Task.CompletedTask;
        }
    }

    // Logging interceptor test
    private class LoggingInterceptor : IInterceptor
    {
        public List<string> Logs { get; } = new();

        public Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request)
        {
            Logs.Add($"Request: {request.Method} {request.RequestUri}");
            return Task.FromResult(request);
        }

        public Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response)
        {
            Logs.Add($"Response: {response.StatusCode}");
            return Task.FromResult(response);
        }

        public Task OnErrorAsync(Exception exception)
        {
            Logs.Add($"Error: {exception.Message}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LoggingInterceptor_LogsCorrectly()
    {
        // Arrange
        var interceptor = new LoggingInterceptor();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var error = new Exception("Test error");

        // Act
        await interceptor.OnRequestAsync(request);
        await interceptor.OnResponseAsync(response);
        await interceptor.OnErrorAsync(error);

        // Assert
        Assert.Equal(3, interceptor.Logs.Count);
        Assert.Contains("Request: GET", interceptor.Logs[0]);
        Assert.Contains("Response: OK", interceptor.Logs[1]);
        Assert.Contains("Error: Test error", interceptor.Logs[2]);
    }
}
