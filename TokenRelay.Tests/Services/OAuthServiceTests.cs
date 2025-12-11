using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.Services;

public class OAuthServiceTests
{
    private readonly Mock<ILogger<OAuthService>> _mockLogger;
    private readonly Mock<IHttpClientService> _mockHttpClientService;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;

    public OAuthServiceTests()
    {
        _mockLogger = new Mock<ILogger<OAuthService>>();
        _mockHttpClientService = new Mock<IHttpClientService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
    }

    [Fact]
    public async Task AcquireTokenAsync_ReturnsToken_WhenRequestIsSuccessful()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.NotNull(token);
        Assert.Equal("mock-access-token-12345", token.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(3600, token.ExpiresIn);
        Assert.Equal("mock-refresh-token", token.RefreshToken);
        Assert.Equal("read write", token.Scope);
    }

    [Fact]
    public async Task AcquireTokenAsync_UsesCachedToken_WhenTokenIsValid()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token1 = await service.AcquireTokenAsync("test-target", target);
        var token2 = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.Same(token1, token2); // Same instance = cache was used

        // Verify HTTP request was only made once
        _mockHttpMessageHandler.Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AcquireTokenAsync_AcquiresNewToken_WhenCachedTokenIsExpired()
    {
        // Arrange
        var tokenResponse1 = TestConfiguration.GetMockTokenResponse("ShortLivedTokenResponse");
        var tokenResponse2 = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");

        var callCount = 0;
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? tokenResponse1 : tokenResponse2;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(response, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token1 = await service.AcquireTokenAsync("test-target", target);
        await Task.Delay(1500); // Wait for token to expire
        var token2 = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.Equal("short-lived-token", token1.AccessToken);
        Assert.Equal("mock-access-token-12345", token2.AccessToken);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenTokenEndpointReturnsError()
    {
        // Arrange
        SetupHttpMessageHandler(HttpStatusCode.Unauthorized, @"{""error"": ""invalid_client""}");

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            async () => await service.AcquireTokenAsync("test-target", target));
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenResponseMissingAccessToken()
    {
        // Arrange
        SetupHttpMessageHandler(HttpStatusCode.OK, @"{""token_type"": ""Bearer""}");

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.AcquireTokenAsync("test-target", target));
    }

    [Fact]
    public async Task AcquireTokenAsync_IsThreadSafe_WhenCalledConcurrently()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act - Call concurrently 10 times
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.AcquireTokenAsync("test-target", target))
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        // Assert - All should return same token instance
        Assert.All(tokens, t => Assert.Same(tokens[0], t));

        // HTTP request should only be made once despite concurrent calls
        _mockHttpMessageHandler.Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task RefreshTokenAsync_AcquiresNewToken_BypassingCache()
    {
        // Arrange
        var tokenResponse1 = @"{""access_token"": ""token-1"", ""token_type"": ""Bearer"", ""expires_in"": 3600}";
        var tokenResponse2 = @"{""access_token"": ""token-2"", ""token_type"": ""Bearer"", ""expires_in"": 3600}";

        var callCount = 0;
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? tokenResponse1 : tokenResponse2;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(response, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token1 = await service.AcquireTokenAsync("test-target", target);
        var token2 = await service.RefreshTokenAsync("test-target", target);

        // Assert
        Assert.Equal("token-1", token1.AccessToken);
        Assert.Equal("token-2", token2.AccessToken);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void ClearTokenCache_RemovesTokenForSpecificTarget()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Cache a token
        var token = service.AcquireTokenAsync("test-target", target).Result;

        // Act
        service.ClearTokenCache("test-target");

        // Verify cache is cleared by checking statistics
        var stats = service.GetCacheStatistics();
        Assert.Equal(0, stats["cachedTokenCount"]);
    }

    [Fact]
    public void GetCacheStatistics_ReturnsAccurateMetrics()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        service.AcquireTokenAsync("test-target", target).Wait(); // Cache miss + acquisition
        service.AcquireTokenAsync("test-target", target).Wait(); // Cache hit
        service.AcquireTokenAsync("test-target", target).Wait(); // Cache hit

        var stats = service.GetCacheStatistics();

        // Assert
        Assert.Equal(1, stats["cachedTokenCount"]);
        Assert.Equal((long)2, stats["cacheHits"]);
        Assert.Equal((long)1, stats["cacheMisses"]);
        Assert.Equal((long)1, stats["tokenAcquisitions"]);
        Assert.True((double)stats["cacheHitRate"] > 0);
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenTargetIsNull()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.AcquireTokenAsync("test-target", null!));
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenAuthTypeIsNotOAuth()
    {
        // Arrange
        var service = CreateService();
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            AuthType = "static",
            AuthData = new Dictionary<string, string>()
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.AcquireTokenAsync("test-target", target));

        Assert.Contains("does not have authType='oauth'", ex.Message);
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenAuthDataIsEmpty()
    {
        // Arrange
        var service = CreateService();
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            AuthType = "oauth",
            AuthData = new Dictionary<string, string>()
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.AcquireTokenAsync("test-target", target));

        Assert.Contains("must have authData configured", ex.Message);
    }

    [Fact]
    public async Task AcquireTokenAsync_ThrowsException_WhenGrantTypeMissing()
    {
        // Arrange
        var service = CreateService();
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            AuthType = "oauth",
            AuthData = new Dictionary<string, string>
            {
                ["username"] = "test"
                // Missing grant_type
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.AcquireTokenAsync("test-target", target));
    }

    [Fact]
    public async Task AcquireTokenAsync_DefaultsTokenType_WhenMissingInResponse()
    {
        // Arrange
        var tokenResponse = @"{
            ""access_token"": ""test-token"",
            ""expires_in"": 3600
        }";

        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.Equal("Bearer", token.TokenType); // Should default to Bearer
    }

    [Fact]
    public async Task AcquireTokenAsync_DefaultsExpiresIn_WhenMissingInResponse()
    {
        // Arrange
        var tokenResponse = @"{
            ""access_token"": ""test-token"",
            ""token_type"": ""Bearer""
        }";

        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.Equal(3600, token.ExpiresIn); // Should default to 3600
    }

    [Fact]
    public async Task AcquireTokenAsync_HandlesCustomTokenType()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("CustomTokenTypeResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token = await service.AcquireTokenAsync("test-target", target);

        // Assert
        Assert.Equal("custom-token", token.AccessToken);
        Assert.Equal("CustomAuth", token.TokenType);
        Assert.Equal(7200, token.ExpiresIn);
    }

    [Fact]
    public async Task AcquireTokenAsync_BuildsDynamicTokenEndpoint_WhenNotProvided()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        HttpRequestMessage? capturedRequest = null;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
            {
                capturedRequest = request;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService();
        var target = TestConfiguration.GetDynamicEndpointTarget();

        // Act
        var token = await service.AcquireTokenAsync("dynamic-target", target);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://api.example.com/oauth/token", capturedRequest!.RequestUri!.ToString());
        Assert.NotNull(token);
    }

    [Fact]
    public async Task AcquireTokenAsync_UsesExplicitTokenEndpoint_WhenProvided()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        HttpRequestMessage? capturedRequest = null;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
            {
                capturedRequest = request;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService();
        var target = TestConfiguration.GetValidOAuthTarget();

        // Act
        var token = await service.AcquireTokenAsync("explicit-target", target);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://auth.example.com/oauth/token", capturedRequest!.RequestUri!.ToString());
        Assert.NotNull(token);
    }

    [Fact]
    public void ClearAllTokens_ClearsAllCachedTokens()
    {
        // Arrange
        var tokenResponse = TestConfiguration.GetMockTokenResponse("ValidTokenResponse");
        SetupHttpMessageHandler(HttpStatusCode.OK, tokenResponse);

        var service = CreateService();
        var target1 = TestConfiguration.GetValidOAuthTarget();
        var target2 = TestConfiguration.GetDynamicEndpointTarget();

        // Cache tokens for multiple targets
        service.AcquireTokenAsync("target1", target1).Wait();
        service.AcquireTokenAsync("target2", target2).Wait();

        var statsBefore = service.GetCacheStatistics();
        Assert.Equal(2, statsBefore["cachedTokenCount"]);

        // Act
        service.ClearAllTokens();

        // Assert
        var statsAfter = service.GetCacheStatistics();
        Assert.Equal(0, statsAfter["cachedTokenCount"]);
    }

    private OAuthService CreateService()
    {
        var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockHttpClientService.Setup(f => f.GetClientForTarget(It.IsAny<TargetConfig>(), It.IsAny<int?>()))
            .Returns(httpClient);

        return new OAuthService(_mockHttpClientService.Object, _mockLogger.Object);
    }

    private void SetupHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
    {
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });
    }
}
