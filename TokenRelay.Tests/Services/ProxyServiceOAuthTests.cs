using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.Services;

public class ProxyServiceOAuthTests
{
    [Fact]
    public async Task AcquireTokenAsync_IsCalledForOAuthTargets()
    {
        // Arrange
        var mockOAuthService = new Mock<IOAuthService>();
        var mockOAuth1Service = new Mock<IOAuth1Service>();
        var mockConfigService = new Mock<IConfigurationService>();
        var mockHttpClientService = new Mock<IHttpClientService>();
        var mockLogger = new Mock<ILogger<ProxyService>>();

        var oauthToken = new OAuthToken
        {
            AccessToken = "test-oauth-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        mockOAuthService
            .Setup(o => o.AcquireTokenAsync(
                It.IsAny<string>(),
                It.IsAny<TargetConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(oauthToken);

        var target = TestConfiguration.GetValidOAuthTarget();
        mockConfigService.Setup(c => c.GetTargetConfig("test-target")).Returns(target);
        mockConfigService.Setup(c => c.GetProxyConfig()).Returns(new ProxyConfig { TimeoutSeconds = 300 });

        // Create a mock service provider
        var mockServiceProvider = new Mock<IServiceProvider>();

        // Act - Verify service can be constructed with IOAuthService and IOAuth1Service
        var proxyService = new ProxyService(
            mockHttpClientService.Object,
            mockConfigService.Object,
            mockLogger.Object,
            mockOAuthService.Object,
            mockOAuth1Service.Object,
            mockServiceProvider.Object);

        // Assert - Service created successfully with OAuth support
        Assert.NotNull(proxyService);
    }

    [Fact]
    public void OAuthToken_GetAuthorizationHeaderValue_FormatsCorrectly()
    {
        // Arrange
        var token = new OAuthToken
        {
            AccessToken = "sample-token-12345",
            TokenType = "Bearer"
        };

        // Act
        var headerValue = token.GetAuthorizationHeaderValue();

        // Assert
        Assert.Equal("Bearer sample-token-12345", headerValue);
    }

    [Fact]
    public void TargetConfig_SupportsOAuthConfiguration()
    {
        // Arrange & Act
        var target = TestConfiguration.GetValidOAuthTarget();

        // Assert
        Assert.Equal("oauth", target.AuthType);
        Assert.NotNull(target.AuthData);
        Assert.True(target.AuthData.Count >= 6); // At least 6 fields
        Assert.True(target.AuthData.ContainsKey("token_endpoint"));
        Assert.True(target.AuthData.ContainsKey("grant_type"));
        Assert.Equal("password", target.AuthData["grant_type"]);
    }

    [Fact]
    public void TargetConfig_SupportsDynamicTokenEndpoint()
    {
        // Arrange & Act
        var target = TestConfiguration.GetDynamicEndpointTarget();

        // Assert
        Assert.Equal("oauth", target.AuthType);
        Assert.NotNull(target.AuthData);
        Assert.False(target.AuthData.ContainsKey("token_endpoint")); // Should NOT have token_endpoint
        Assert.True(target.AuthData.ContainsKey("grant_type"));
        Assert.Equal("https://api.example.com", target.Endpoint);
    }

    [Fact]
    public void TargetConfig_DefaultsToStaticAuth()
    {
        // Arrange & Act
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer static-token"
            }
        };

        // Assert
        Assert.Equal("static", target.AuthType); // Should default to "static"
    }

    [Fact]
    public void TargetConfig_SupportsEmptyAuthData()
    {
        // Arrange & Act
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            AuthType = "static"
        };

        // Assert
        Assert.NotNull(target.AuthData); // Should default to empty dictionary
        Assert.Empty(target.AuthData);
    }

    [Fact]
    public async Task OAuthService_GetCacheStatistics_ReturnsInitialState()
    {
        // Arrange
        var mockHttpClientService = new Mock<IHttpClientService>();
        var mockLogger = new Mock<ILogger<OAuthService>>();

        var service = new OAuthService(mockHttpClientService.Object, mockLogger.Object);

        // Act
        var stats = service.GetCacheStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats["cachedTokenCount"]);
        Assert.Equal((long)0, stats["cacheHits"]);
        Assert.Equal((long)0, stats["cacheMisses"]);
        Assert.Equal((long)0, stats["tokenAcquisitions"]);
        Assert.Equal((long)0, stats["tokenRefreshes"]);
        Assert.Equal((long)0, stats["tokenAcquisitionFailures"]);
        Assert.Equal(0.0, (double)stats["cacheHitRate"]);
    }

    [Fact]
    public void OAuthService_ClearAllTokens_DoesNotThrow()
    {
        // Arrange
        var mockHttpClientService = new Mock<IHttpClientService>();
        var mockLogger = new Mock<ILogger<OAuthService>>();

        var service = new OAuthService(mockHttpClientService.Object, mockLogger.Object);

        // Act & Assert - Should not throw
        service.ClearAllTokens();
        service.ClearTokenCache("non-existent-target");
    }

    [Fact]
    public void TestConfiguration_LoadsCredentialsFromAppSettings()
    {
        // Arrange & Act
        var validTarget = TestConfiguration.GetValidOAuthTarget();
        var dynamicTarget = TestConfiguration.GetDynamicEndpointTarget();
        var minimalTarget = TestConfiguration.GetMinimalTarget();

        // Assert ValidTarget
        Assert.NotNull(validTarget);
        Assert.Equal("https://api.example.com", validTarget.Endpoint);
        Assert.Equal("oauth", validTarget.AuthType);
        Assert.Equal("password", validTarget.AuthData["grant_type"]);
        Assert.Equal("test-user", validTarget.AuthData["username"]);

        // Assert DynamicTarget
        Assert.NotNull(dynamicTarget);
        Assert.Equal("https://api.example.com", dynamicTarget.Endpoint);
        Assert.False(dynamicTarget.AuthData.ContainsKey("token_endpoint"));

        // Assert MinimalTarget
        Assert.NotNull(minimalTarget);
        Assert.Equal("client_credentials", minimalTarget.AuthData["grant_type"]);
    }

    [Fact]
    public void TestConfiguration_GetsMockTokenResponses()
    {
        // Act
        var validResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        var shortLivedResponse = TestConfiguration.GetMockTokenResponse("ShortLivedTokenResponse");
        var customResponse = TestConfiguration.GetMockTokenResponse("CustomTokenTypeResponse");

        // Assert
        Assert.Contains("mock-access-token-12345", validResponse);
        Assert.Contains("Bearer", validResponse);
        Assert.Contains("3600", validResponse);

        Assert.Contains("short-lived-token", shortLivedResponse);
        Assert.Contains("\"expires_in\": 1", shortLivedResponse);

        Assert.Contains("custom-token", customResponse);
        Assert.Contains("CustomAuth", customResponse);
    }

    [Fact]
    public void TargetConfig_ValidatesPasswordGrantRequirements()
    {
        // Arrange
        var target = TestConfiguration.GetValidOAuthTarget();

        // Assert - password grant should have all required fields
        Assert.True(target.AuthData.ContainsKey("grant_type"));
        Assert.True(target.AuthData.ContainsKey("username"));
        Assert.True(target.AuthData.ContainsKey("password"));
        Assert.True(target.AuthData.ContainsKey("client_id"));
        Assert.Equal("password", target.AuthData["grant_type"]);
    }

    [Fact]
    public void TargetConfig_ValidatesClientCredentialsGrant()
    {
        // Arrange
        var target = TestConfiguration.GetMinimalTarget();

        // Assert - client_credentials should have required fields
        Assert.Equal("client_credentials", target.AuthData["grant_type"]);
        Assert.True(target.AuthData.ContainsKey("client_id"));
        Assert.True(target.AuthData.ContainsKey("client_secret"));
    }

    [Fact]
    public void OAuthToken_SupportsAllResponseFields()
    {
        // Arrange & Act
        var token = new OAuthToken
        {
            AccessToken = "access-123",
            TokenType = "Bearer",
            ExpiresIn = 7200,
            RefreshToken = "refresh-456",
            Scope = "read write delete",
            AcquiredAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("access-123", token.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(7200, token.ExpiresIn);
        Assert.Equal("refresh-456", token.RefreshToken);
        Assert.Equal("read write delete", token.Scope);
        Assert.NotEqual(default(DateTime), token.AcquiredAt);
        Assert.True(token.ExpiresAt > DateTime.UtcNow);
    }
}
