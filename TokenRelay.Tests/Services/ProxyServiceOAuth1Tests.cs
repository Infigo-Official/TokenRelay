using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.Services;

public class ProxyServiceOAuth1Tests
{
    [Fact]
    public async Task ProxyService_CanBeConstructed_WithOAuth1Service()
    {
        // Arrange
        var mockOAuthService = new Mock<IOAuthService>();
        var mockOAuth1Service = new Mock<IOAuth1Service>();
        var mockConfigService = new Mock<IConfigurationService>();
        var mockHttpClientService = new Mock<IHttpClientService>();
        var mockLogger = new Mock<ILogger<ProxyService>>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockConfigService.Setup(c => c.GetProxyConfig())
            .Returns(new ProxyConfig { TimeoutSeconds = 300 });

        // Act
        var proxyService = new ProxyService(
            mockHttpClientService.Object,
            mockConfigService.Object,
            mockLogger.Object,
            mockOAuthService.Object,
            mockOAuth1Service.Object,
            mockServiceProvider.Object);

        // Assert
        Assert.NotNull(proxyService);
    }

    [Fact]
    public void TargetConfig_SupportsOAuth1Configuration()
    {
        // Arrange & Act
        var target = new TargetConfig
        {
            Endpoint = "https://1234567.restlets.api.netsuite.com/app/site/hosting/restlet.nl",
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "test-consumer-key",
                ["consumer_secret"] = "test-consumer-secret",
                ["token_id"] = "test-token-id",
                ["token_secret"] = "test-token-secret",
                ["realm"] = "1234567_SB1",
                ["signature_method"] = "HMAC-SHA256"
            }
        };

        // Assert
        Assert.Equal("oauth1", target.AuthType);
        Assert.True(target.AuthData.ContainsKey("consumer_key"));
        Assert.True(target.AuthData.ContainsKey("consumer_secret"));
        Assert.True(target.AuthData.ContainsKey("token_id"));
        Assert.True(target.AuthData.ContainsKey("token_secret"));
        Assert.True(target.AuthData.ContainsKey("realm"));
    }

    [Fact]
    public void TargetConfig_SupportsQueryParams()
    {
        // Arrange & Act
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com/resource",
            QueryParams = new Dictionary<string, string>
            {
                ["script"] = "customscript_test",
                ["deploy"] = "customdeploy_test"
            }
        };

        // Assert
        Assert.NotNull(target.QueryParams);
        Assert.Equal(2, target.QueryParams.Count);
        Assert.Equal("customscript_test", target.QueryParams["script"]);
        Assert.Equal("customdeploy_test", target.QueryParams["deploy"]);
    }

    [Fact]
    public void TargetConfig_QueryParams_DefaultsToEmptyDictionary()
    {
        // Arrange & Act
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com/resource"
        };

        // Assert
        Assert.NotNull(target.QueryParams);
        Assert.Empty(target.QueryParams);
    }

    [Fact]
    public async Task OAuth1Service_GeneratesHeader_ForNetSuiteConfig()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OAuth1Service>>();
        var service = new OAuth1Service(mockLogger.Object);

        var target = new TargetConfig
        {
            Endpoint = "https://1234567.restlets.api.netsuite.com/app/site/hosting/restlet.nl",
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "abc123",
                ["consumer_secret"] = "secret1",
                ["token_id"] = "token123",
                ["token_secret"] = "tokensecret1",
                ["realm"] = "1234567_SB1",
                ["signature_method"] = "HMAC-SHA256"
            }
        };

        // Act
        var header = await service.GenerateAuthorizationHeaderAsync(
            "netsuite-test",
            target,
            HttpMethod.Post,
            "https://1234567.restlets.api.netsuite.com/app/site/hosting/restlet.nl?script=test&deploy=1");

        // Assert
        Assert.StartsWith("OAuth ", header);
        Assert.Contains("realm=\"1234567_SB1\"", header);
        Assert.Contains("oauth_consumer_key=\"abc123\"", header);
        Assert.Contains("oauth_token=\"token123\"", header);
        Assert.Contains("oauth_signature_method=\"HMAC-SHA256\"", header);
        Assert.Contains("oauth_version=\"1.0\"", header);
        Assert.Contains("oauth_signature=", header);
    }

    [Fact]
    public void OAuth1Service_ValidatesConfiguration_ThrowsForMissingFields()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OAuth1Service>>();
        var service = new OAuth1Service(mockLogger.Object);

        var invalidTarget = new TargetConfig
        {
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "key"
                // Missing other required fields
            }
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateConfiguration("test-target", invalidTarget));
    }

    [Fact]
    public void OAuth1Service_ValidatesConfiguration_AcceptsValidConfig()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OAuth1Service>>();
        var service = new OAuth1Service(mockLogger.Object);

        var validTarget = new TargetConfig
        {
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "key",
                ["consumer_secret"] = "secret",
                ["token_id"] = "token",
                ["token_secret"] = "tokensecret",
                ["realm"] = "1234567_SB1"
            }
        };

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            service.ValidateConfiguration("test-target", validTarget));

        Assert.Null(exception);
    }

    [Fact]
    public async Task OAuth1Service_GeneratesUniqueSignaturesPerRequest()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OAuth1Service>>();
        var service = new OAuth1Service(mockLogger.Object);

        var target = new TargetConfig
        {
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "key",
                ["consumer_secret"] = "secret",
                ["token_id"] = "token",
                ["token_secret"] = "tokensecret",
                ["realm"] = "1234567"
            }
        };

        // Act
        var header1 = await service.GenerateAuthorizationHeaderAsync(
            "test", target, HttpMethod.Get, "https://api.example.com/resource");
        var header2 = await service.GenerateAuthorizationHeaderAsync(
            "test", target, HttpMethod.Get, "https://api.example.com/resource");

        // Assert - Each request should have unique nonce and potentially different timestamp
        Assert.NotEqual(header1, header2);
    }

    [Fact]
    public async Task OAuth1Service_SupportsHmacSha1()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OAuth1Service>>();
        var service = new OAuth1Service(mockLogger.Object);

        var target = new TargetConfig
        {
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>
            {
                ["consumer_key"] = "key",
                ["consumer_secret"] = "secret",
                ["token_id"] = "token",
                ["token_secret"] = "tokensecret",
                ["realm"] = "1234567",
                ["signature_method"] = "HMAC-SHA1"
            }
        };

        // Act
        var header = await service.GenerateAuthorizationHeaderAsync(
            "test", target, HttpMethod.Get, "https://api.example.com/resource");

        // Assert
        Assert.Contains("oauth_signature_method=\"HMAC-SHA1\"", header);
    }
}
