using System;
using TokenRelay.Models;
using Xunit;

namespace TokenRelay.Tests.Models;

public class OAuthTokenTests
{
    [Fact]
    public void IsExpired_ReturnsFalse_WhenTokenIsValid()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            AcquiredAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.False(token.IsExpired());
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenTokenIsExpired()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            AcquiredAt = DateTime.UtcNow.AddHours(-2) // 2 hours ago
        };

        // Act & Assert
        Assert.True(token.IsExpired());
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenTokenIsWithinBufferWindow()
    {
        // Arrange - token expires in 30 seconds, buffer is 60 seconds
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 30,
            AcquiredAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.True(token.IsExpired(bufferSeconds: 60));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenTokenIsOutsideBufferWindow()
    {
        // Arrange - token expires in 120 seconds, buffer is 60 seconds
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 120,
            AcquiredAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.False(token.IsExpired(bufferSeconds: 60));
    }

    [Fact]
    public void GetAuthorizationHeaderValue_ReturnsCorrectFormat()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "my-access-token",
            TokenType = "Bearer"
        };

        // Act
        var result = token.GetAuthorizationHeaderValue();

        // Assert
        Assert.Equal("Bearer my-access-token", result);
    }

    [Fact]
    public void GetAuthorizationHeaderValue_SupportsCustomTokenType()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "custom-token",
            TokenType = "CustomAuth"
        };

        // Act
        var result = token.GetAuthorizationHeaderValue();

        // Assert
        Assert.Equal("CustomAuth custom-token", result);
    }

    [Fact]
    public void ExpiresAt_CalculatesCorrectly()
    {
        // Arrange
        var acquiredAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            AcquiredAt = acquiredAt
        };

        // Act
        var expiresAt = token.ExpiresAt;

        // Assert
        Assert.Equal(new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), expiresAt);
    }

    [Fact]
    public void TokenType_DefaultsToBearer()
    {
        // Arrange & Act
        var token = new OAuthToken
        {
            AccessToken = "test-token"
        };

        // Assert
        Assert.Equal("Bearer", token.TokenType);
    }

    [Fact]
    public void ExpiresIn_DefaultsTo3600()
    {
        // Arrange & Act
        var token = new OAuthToken
        {
            AccessToken = "test-token"
        };

        // Assert
        Assert.Equal(3600, token.ExpiresIn);
    }

    [Fact]
    public void IsExpired_HandlesZeroBuffer()
    {
        // Arrange - token expires in 1 second, no buffer
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 1,
            AcquiredAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.False(token.IsExpired(bufferSeconds: 0));
    }

    [Fact]
    public void GetAuthorizationHeaderValue_HandlesEmptyToken()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "",
            TokenType = "Bearer"
        };

        // Act
        var result = token.GetAuthorizationHeaderValue();

        // Assert
        Assert.Equal("Bearer ", result);
    }

    [Fact]
    public void RefreshToken_CanBeNull()
    {
        // Arrange & Act
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            RefreshToken = null
        };

        // Assert
        Assert.Null(token.RefreshToken);
    }

    [Fact]
    public void Scope_CanBeNull()
    {
        // Arrange & Act
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            Scope = null
        };

        // Assert
        Assert.Null(token.Scope);
    }

    [Fact]
    public void ExpiresAt_UpdatesWhenAcquiredAtChanges()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            AcquiredAt = DateTime.UtcNow
        };

        var firstExpiresAt = token.ExpiresAt;

        // Act - change AcquiredAt
        token.AcquiredAt = DateTime.UtcNow.AddHours(1);
        var secondExpiresAt = token.ExpiresAt;

        // Assert - ExpiresAt should be different
        Assert.NotEqual(firstExpiresAt, secondExpiresAt);
        Assert.True(secondExpiresAt > firstExpiresAt);
    }
}
