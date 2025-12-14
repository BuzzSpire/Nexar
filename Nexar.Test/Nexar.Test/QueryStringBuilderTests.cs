using Nexar.Helpers;

namespace Nexar.Test;

/// <summary>
/// Unit tests for QueryStringBuilder
/// </summary>
public class QueryStringBuilderTests
{
    [Fact]
    public void Build_WithSingleParameter_ReturnsCorrectQueryString()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();

        // Act
        var result = builder.Add("key", "value").Build();

        // Assert
        Assert.Equal("?key=value", result);
    }

    [Fact]
    public void Build_WithMultipleParameters_ReturnsCorrectQueryString()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();

        // Act
        var result = builder
            .Add("page", "1")
            .Add("limit", "10")
            .Add("sort", "name")
            .Build();

        // Assert
        Assert.Equal("?page=1&limit=10&sort=name", result);
    }

    [Fact]
    public void Build_WithSpecialCharacters_EncodesCorrectly()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();

        // Act
        var result = builder
            .Add("name", "John Doe")
            .Add("email", "john@example.com")
            .Build();

        // Assert
        // Space can be encoded as either + or %20, both are valid
        Assert.True(result.Contains("John+Doe") || result.Contains("John%20Doe"),
            "Name should be URL encoded (space as + or %20)");
        Assert.Contains("john%40example.com", result);
    }

    [Fact]
    public void Build_WithNoParameters_ReturnsEmptyString()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();

        // Act
        var result = builder.Build();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void AddRange_WithDictionary_AddsAllParameters()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();
        var parameters = new Dictionary<string, string>
        {
            { "page", "1" },
            { "limit", "20" },
            { "filter", "active" }
        };

        // Act
        var result = builder.AddRange(parameters).Build();

        // Assert
        Assert.Contains("page=1", result);
        Assert.Contains("limit=20", result);
        Assert.Contains("filter=active", result);
    }

    [Fact]
    public void Create_ReturnsNewInstance()
    {
        // Act
        var builder1 = QueryStringBuilder.Create();
        var builder2 = QueryStringBuilder.Create();

        // Assert
        Assert.NotNull(builder1);
        Assert.NotNull(builder2);
        Assert.NotSame(builder1, builder2);
    }

    [Fact]
    public void Build_ChainableMethods_WorksCorrectly()
    {
        // Act
        var result = QueryStringBuilder.Create()
            .Add("search", "nexar")
            .Add("type", "library")
            .Add("lang", "csharp")
            .Build();

        // Assert
        Assert.StartsWith("?", result);
        Assert.Contains("search=nexar", result);
        Assert.Contains("type=library", result);
        Assert.Contains("lang=csharp", result);
    }

    [Fact]
    public void Build_WithNumericValues_ConvertsToString()
    {
        // Arrange
        var builder = QueryStringBuilder.Create();

        // Act
        var result = builder
            .Add("page", "1")
            .Add("limit", "100")
            .Build();

        // Assert
        Assert.Contains("page=1", result);
        Assert.Contains("limit=100", result);
    }
}
