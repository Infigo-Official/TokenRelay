using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.Services;

public class OAuth1ServiceTests
{
    private readonly Mock<ILogger<OAuth1Service>> _mockLogger;
    private readonly OAuth1Service _service;

    public OAuth1ServiceTests()
    {
        _mockLogger = new Mock<ILogger<OAuth1Service>>();
        _service = new OAuth1Service(_mockLogger.Object);
    }

    #region GenerateAuthorizationHeaderAsync Tests

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_ReturnsValidHeader_WhenConfigIsValid()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act
        var header = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Post,
            "https://api.example.com/resource");

        // Assert
        Assert.NotNull(header);
        Assert.StartsWith("OAuth ", header);
        Assert.Contains("oauth_consumer_key=", header);
        Assert.Contains("oauth_token=", header);
        Assert.Contains("oauth_signature_method=", header);
        Assert.Contains("oauth_timestamp=", header);
        Assert.Contains("oauth_nonce=", header);
        Assert.Contains("oauth_signature=", header);
        Assert.Contains("realm=", header);
    }

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_UsesHmacSha256_ByDefault()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("signature_method"); // Remove to use default

        // Act
        var header = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource");

        // Assert
        Assert.Contains("oauth_signature_method=\"HMAC-SHA256\"", header);
    }

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_UsesHmacSha1_WhenSpecified()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData["signature_method"] = "HMAC-SHA1";

        // Act
        var header = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource");

        // Assert
        Assert.Contains("oauth_signature_method=\"HMAC-SHA1\"", header);
    }

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_IncludesRealm()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act
        var header = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Post,
            "https://api.example.com/resource");

        // Assert
        Assert.Contains("realm=\"1234567_SB1\"", header);
    }

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_HandlesQueryStringParams()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act
        var header = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource?script=test&deploy=1");

        // Assert
        Assert.NotNull(header);
        Assert.Contains("oauth_signature=", header);
    }

    [Fact]
    public async Task GenerateAuthorizationHeaderAsync_GeneratesUniqueNonceForEachCall()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act
        var header1 = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource");

        var header2 = await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource");

        // Assert - Extract nonces and verify they're different
        Assert.NotEqual(header1, header2);
    }

    #endregion

    #region ValidateConfiguration Tests

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenConsumerKeyMissing()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("consumer_key");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("consumer_key", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenConsumerSecretMissing()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("consumer_secret");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("consumer_secret", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenTokenIdMissing()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("token_id");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("token_id", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenTokenSecretMissing()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("token_secret");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("token_secret", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenRealmMissing()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData.Remove("realm");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("realm", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenAuthDataIsNull()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            AuthType = "oauth1",
            AuthData = null!
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("AuthData", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_ThrowsException_WhenInvalidSignatureMethod()
    {
        // Arrange
        var target = CreateValidOAuth1Target();
        target.AuthData["signature_method"] = "INVALID-METHOD";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Contains("signature_method", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_Succeeds_WhenAllRequiredFieldsPresent()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            _service.ValidateConfiguration("test-target", target));

        Assert.Null(exception);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_ReturnsInitialZeroStats()
    {
        // Act
        var stats = _service.GetStatistics();

        // Assert
        Assert.Equal(0L, stats["totalRequests"]);
        Assert.Equal(0L, stats["successfulRequests"]);
        Assert.Equal(0L, stats["failedRequests"]);
        Assert.Equal(0.0, stats["successRate"]);
    }

    [Fact]
    public async Task GetStatistics_TracksSuccessfulRequests()
    {
        // Arrange
        var target = CreateValidOAuth1Target();

        // Act
        await _service.GenerateAuthorizationHeaderAsync(
            "test-target",
            target,
            HttpMethod.Get,
            "https://api.example.com/resource");

        var stats = _service.GetStatistics();

        // Assert
        Assert.Equal(1L, stats["totalRequests"]);
        Assert.Equal(1L, stats["successfulRequests"]);
        Assert.Equal(0L, stats["failedRequests"]);
        Assert.Equal(100.0, stats["successRate"]);
    }

    [Fact]
    public async Task GetStatistics_TracksFailedRequests()
    {
        // Arrange
        var invalidTarget = new TargetConfig
        {
            AuthType = "oauth1",
            AuthData = new Dictionary<string, string>() // Missing required fields
        };

        // Act
        try
        {
            await _service.GenerateAuthorizationHeaderAsync(
                "test-target",
                invalidTarget,
                HttpMethod.Get,
                "https://api.example.com/resource");
        }
        catch
        {
            // Expected to fail
        }

        var stats = _service.GetStatistics();

        // Assert
        Assert.Equal(1L, stats["totalRequests"]);
        Assert.Equal(0L, stats["successfulRequests"]);
        Assert.Equal(1L, stats["failedRequests"]);
        Assert.Equal(0.0, stats["successRate"]);
    }

    #endregion

    #region Static Method Tests (Internal)

    [Fact]
    public void GenerateNonce_ReturnsUniqueValues()
    {
        // Act
        var nonce1 = OAuth1Service.GenerateNonce();
        var nonce2 = OAuth1Service.GenerateNonce();

        // Assert
        Assert.NotEqual(nonce1, nonce2);
        Assert.False(string.IsNullOrEmpty(nonce1));
        Assert.False(string.IsNullOrEmpty(nonce2));
    }

    [Fact]
    public void GenerateTimestamp_ReturnsValidUnixTimestamp()
    {
        // Act
        var timestamp = OAuth1Service.GenerateTimestamp();

        // Assert
        Assert.True(long.TryParse(timestamp, out var timestampValue));
        Assert.True(timestampValue > 0);

        // Verify it's close to current time (within 10 seconds)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.True(Math.Abs(now - timestampValue) < 10);
    }

    [Fact]
    public void NormalizeRequestUrl_RemovesDefaultHttpPort()
    {
        // Arrange
        var uri = new Uri("http://example.com:80/path");

        // Act
        var normalized = OAuth1Service.NormalizeRequestUrl(uri);

        // Assert
        Assert.Equal("http://example.com/path", normalized);
    }

    [Fact]
    public void NormalizeRequestUrl_RemovesDefaultHttpsPort()
    {
        // Arrange
        var uri = new Uri("https://example.com:443/path");

        // Act
        var normalized = OAuth1Service.NormalizeRequestUrl(uri);

        // Assert
        Assert.Equal("https://example.com/path", normalized);
    }

    [Fact]
    public void NormalizeRequestUrl_KeepsNonDefaultPort()
    {
        // Arrange
        var uri = new Uri("https://example.com:8443/path");

        // Act
        var normalized = OAuth1Service.NormalizeRequestUrl(uri);

        // Assert
        Assert.Equal("https://example.com:8443/path", normalized);
    }

    [Fact]
    public void NormalizeRequestUrl_LowercasesSchemeAndHost()
    {
        // Arrange
        var uri = new Uri("HTTPS://EXAMPLE.COM/Path");

        // Act
        var normalized = OAuth1Service.NormalizeRequestUrl(uri);

        // Assert
        Assert.Equal("https://example.com/Path", normalized);
    }

    [Fact]
    public void PercentEncode_EncodesSpecialCharacters()
    {
        // Act & Assert
        Assert.Equal("hello%20world", OAuth1Service.PercentEncode("hello world"));
        Assert.Equal("a%26b", OAuth1Service.PercentEncode("a&b"));
        Assert.Equal("100%25", OAuth1Service.PercentEncode("100%"));
        Assert.Equal("test%3Dvalue", OAuth1Service.PercentEncode("test=value"));
    }

    [Fact]
    public void PercentEncode_DoesNotEncodeUnreservedCharacters()
    {
        // Act & Assert
        Assert.Equal("ABCabc123", OAuth1Service.PercentEncode("ABCabc123"));
        Assert.Equal("a-b_c.d~e", OAuth1Service.PercentEncode("a-b_c.d~e"));
    }

    [Fact]
    public void PercentEncode_HandlesEmptyString()
    {
        // Act & Assert
        Assert.Equal(string.Empty, OAuth1Service.PercentEncode(string.Empty));
        Assert.Equal(string.Empty, OAuth1Service.PercentEncode(null!));
    }

    [Fact]
    public void GenerateSignatureBaseString_FormatsCorrectly()
    {
        // Arrange
        var parameters = new Dictionary<string, string>
        {
            ["oauth_consumer_key"] = "key",
            ["oauth_token"] = "token",
            ["b_param"] = "value2",
            ["a_param"] = "value1"
        };

        // Act
        var baseString = OAuth1Service.GenerateSignatureBaseString(
            "POST",
            "https://api.example.com/resource",
            parameters);

        // Assert
        Assert.StartsWith("POST&", baseString);
        Assert.Contains("https%3A%2F%2Fapi.example.com%2Fresource", baseString);
        // Parameters should be sorted alphabetically
        Assert.Contains("a_param%3Dvalue1", baseString);
    }

    [Fact]
    public void CollectAndNormalizeParameters_SortsAlphabetically()
    {
        // Arrange
        var parameters = new Dictionary<string, string>
        {
            ["z_param"] = "last",
            ["a_param"] = "first",
            ["m_param"] = "middle"
        };

        // Act
        var baseString = OAuth1Service.GenerateSignatureBaseString(
            "GET",
            "https://api.example.com",
            parameters);

        // Assert - Verify alphabetical order in the encoded params
        var paramsIndex = baseString.LastIndexOf('&') + 1;
        var encodedParams = Uri.UnescapeDataString(baseString[paramsIndex..]);

        // a_param should come before m_param, which should come before z_param
        var aIndex = encodedParams.IndexOf("a_param");
        var mIndex = encodedParams.IndexOf("m_param");
        var zIndex = encodedParams.IndexOf("z_param");

        Assert.True(aIndex < mIndex);
        Assert.True(mIndex < zIndex);
    }

    #endregion

    #region Helper Methods

    private static TargetConfig CreateValidOAuth1Target()
    {
        return new TargetConfig
        {
            Endpoint = "https://api.netsuite.com/app/site/hosting/restlet.nl",
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
    }

    #endregion
}
