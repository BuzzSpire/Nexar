using System.Text;
using Nexar.Auth;

namespace Nexar.Test;

/// <summary>
/// Unit tests for AuthHelper
/// </summary>
public class AuthHelperTests
{
    [Fact]
    public void Bearer_WithToken_ReturnsFormattedString()
    {
        // Arrange
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";

        // Act
        var result = AuthHelper.Bearer(token);

        // Assert
        Assert.Equal($"Bearer {token}", result);
    }

    [Fact]
    public void Bearer_WithEmptyToken_ReturnsFormattedString()
    {
        // Arrange
        var token = "";

        // Act
        var result = AuthHelper.Bearer(token);

        // Assert
        Assert.Equal("Bearer ", result);
    }

    [Fact]
    public void Basic_WithCredentials_ReturnsBase64EncodedString()
    {
        // Arrange
        var username = "testuser";
        var password = "testpass";
        var expectedCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}"));

        // Act
        var result = AuthHelper.Basic(username, password);

        // Assert
        Assert.Equal($"Basic {expectedCredentials}", result);
    }

    [Fact]
    public void Basic_WithSpecialCharacters_EncodesCorrectly()
    {
        // Arrange
        var username = "user@example.com";
        var password = "p@ssw0rd!";
        var expectedCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}"));

        // Act
        var result = AuthHelper.Basic(username, password);

        // Assert
        Assert.Equal($"Basic {expectedCredentials}", result);
        Assert.StartsWith("Basic ", result);
    }

    [Fact]
    public void ApiKey_WithHeaderAndKey_ReturnsKeyValuePair()
    {
        // Arrange
        var headerName = "X-API-Key";
        var apiKey = "abc123xyz";

        // Act
        var result = AuthHelper.ApiKey(headerName, apiKey);

        // Assert
        Assert.Equal(headerName, result.Key);
        Assert.Equal(apiKey, result.Value);
    }

    [Fact]
    public void ApiKey_WithCustomHeaderName_ReturnsCorrectPair()
    {
        // Arrange
        var headerName = "Authorization";
        var apiKey = "secret-key-12345";

        // Act
        var result = AuthHelper.ApiKey(headerName, apiKey);

        // Assert
        Assert.Equal(headerName, result.Key);
        Assert.Equal(apiKey, result.Value);
    }

    [Fact]
    public void Basic_WithEmptyCredentials_ReturnsValidFormat()
    {
        // Arrange
        var username = "";
        var password = "";

        // Act
        var result = AuthHelper.Basic(username, password);

        // Assert
        Assert.StartsWith("Basic ", result);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("token123", "Bearer token123")]
    [InlineData("jwt.token.here", "Bearer jwt.token.here")]
    [InlineData("", "Bearer ")]
    public void Bearer_WithVariousTokens_ReturnsCorrectFormat(string token, string expected)
    {
        // Act
        var result = AuthHelper.Bearer(token);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("user", "pass")]
    [InlineData("admin", "admin123")]
    [InlineData("test@example.com", "P@ssw0rd!")]
    public void Basic_WithVariousCredentials_ReturnsValidBase64(string username, string password)
    {
        // Act
        var result = AuthHelper.Basic(username, password);

        // Assert
        Assert.StartsWith("Basic ", result);
        Assert.True(result.Length > 6); // "Basic " + some base64 string
    }
}
