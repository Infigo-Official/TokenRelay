using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TokenRelay.Tests.Integration.OAuth1;

/// <summary>
/// Integration tests for OAuth1 authentication through TokenRelay proxy.
///
/// These tests verify that:
/// 1. TokenRelay correctly generates OAuth1 signatures
/// 2. The mock OAuth1 server validates the signatures
/// 3. End-to-end OAuth1 flow works through the proxy
///
/// Prerequisites:
/// - Docker and docker-compose must be installed
/// - Port 5193 (TokenRelay) and 8191 (OAuth1 server) must be available
///
/// Running tests:
///   dotnet test --filter "Category=OAuth1"
///
/// Manual container management (for debugging):
///   Set OAUTH1_SKIP_DOCKER=true environment variable
///   Start containers: docker-compose -f test/docker/docker-compose.oauth1-integration.yml up -d --build
/// </summary>
[Collection("OAuth1 Integration Tests")]
[Trait("Category", "Integration")]
[Trait("Category", "OAuth1")]
public class OAuth1IntegrationTests
{
    private readonly OAuth1IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OAuth1IntegrationTests(OAuth1IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Basic OAuth1 Flow Tests

    [Fact]
    public async Task Proxy_OAuth1Target_ValidatesSignatureSuccessfully()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/resource");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1Response>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
    }

    [Fact]
    public async Task Proxy_OAuth1_GET_Request_ValidSignature()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.Equal("GET", result.Request?.Method);
    }

    [Fact]
    public async Task Proxy_OAuth1_POST_Request_ValidSignature()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Post, "oauth1-test", "/oauth1/echo");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { test = "data", value = 123 }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.Equal("POST", result.Request?.Method);
    }

    [Fact]
    public async Task Proxy_OAuth1_PUT_Request_ValidSignature()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Put, "oauth1-test", "/oauth1/resource");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { id = 1, name = "updated" }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_OAuth1_DELETE_Request_ValidSignature()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Delete, "oauth1-test", "/oauth1/resource");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Signature Method Tests

    [Fact]
    public async Task Proxy_OAuth1_UsesHmacSha256_WhenConfigured()
    {
        // Arrange - oauth1-test target uses HMAC-SHA256
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("HMAC-SHA256", result.OAuthParams?.SignatureMethod);
    }

    [Fact]
    public async Task Proxy_OAuth1_UsesHmacSha1_WhenConfigured()
    {
        // Arrange - oauth1-sha1-test target uses HMAC-SHA1
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-sha1-test", "/oauth1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("HMAC-SHA1", result.OAuthParams?.SignatureMethod);
    }

    #endregion

    #region OAuth Parameters Tests

    [Fact]
    public async Task Proxy_OAuth1_IncludesAllRequiredParameters()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.OAuthParams);

        // Verify all required OAuth1 parameters are present
        Assert.Equal("test-consumer-key", result.OAuthParams.ConsumerKey);
        Assert.Equal("test-token-id", result.OAuthParams.Token);
        Assert.Equal("test-realm", result.OAuthParams.Realm);
        Assert.NotNull(result.OAuthParams.Timestamp);
        Assert.NotNull(result.OAuthParams.Nonce);
        Assert.Equal("1.0", result.OAuthParams.Version);
    }

    [Fact]
    public async Task Proxy_OAuth1_GeneratesUniqueNoncePerRequest()
    {
        // Arrange & Act
        var request1 = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo");
        var response1 = await _fixture.HttpClient.SendAsync(request1);
        var content1 = await response1.Content.ReadAsStringAsync();

        var request2 = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo");
        var response2 = await _fixture.HttpClient.SendAsync(request2);
        var content2 = await response2.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var result1 = JsonSerializer.Deserialize<OAuth1EchoResponse>(content1, JsonOptions);
        var result2 = JsonSerializer.Deserialize<OAuth1EchoResponse>(content2, JsonOptions);

        Assert.NotNull(result1?.OAuthParams?.Nonce);
        Assert.NotNull(result2?.OAuthParams?.Nonce);
        Assert.NotEqual(result1.OAuthParams.Nonce, result2.OAuthParams.Nonce);

        _output.WriteLine($"Nonce 1: {result1.OAuthParams.Nonce}");
        _output.WriteLine($"Nonce 2: {result2.OAuthParams.Nonce}");
    }

    #endregion

    #region Query Parameter Tests

    [Fact]
    public async Task Proxy_OAuth1_QueryParams_IncludedInSignature()
    {
        // Arrange - Include query params in request
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/echo?param1=value1&param2=value2");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert - Signature should be valid (query params included in signature)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
    }

    [Fact]
    public async Task Proxy_OAuth1_ConfiguredQueryParams_MergedAndIncludedInSignature()
    {
        // Arrange - oauth1-with-query-params target has preconfigured queryParams
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-with-query-params", "/oauth1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert - Signature should be valid with configured query params
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);

        // The configured queryParams (script, deploy) should be in the request
        Assert.NotNull(result.Request?.QueryParams);
    }

    [Fact]
    public async Task Proxy_OAuth1_SpecialCharactersInQueryParams_ProperlyEncoded()
    {
        // Arrange - Query params with special characters
        var request = _fixture.CreateProxyRequest(
            HttpMethod.Get,
            "oauth1-test",
            "/oauth1/echo?filter=name%3Dtest&format=json%26xml");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert - Should still validate correctly
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Mock OAuth1 Server Direct Tests

    [Fact]
    public async Task MockOAuth1Server_Health_ReturnsOk()
    {
        // Arrange & Act
        var response = await _fixture.HttpClient.GetAsync($"{_fixture.OAuth1ServerBaseUrl}/health");
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Health Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MockOAuth1Server_WithoutAuth_Returns401()
    {
        // Arrange - Direct call to OAuth1 server without Authorization header
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.OAuth1ServerBaseUrl}/oauth1/resource");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1Response>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("error", result.Status);
    }

    [Fact]
    public async Task MockOAuth1Server_WithInvalidSignature_Returns401()
    {
        // Arrange - Direct call with invalid OAuth1 header (use current timestamp to pass timestamp validation)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.OAuth1ServerBaseUrl}/oauth1/resource");
        request.Headers.TryAddWithoutValidation("Authorization",
            $"OAuth realm=\"test-realm\", " +
            $"oauth_consumer_key=\"test-consumer-key\", " +
            $"oauth_token=\"test-token-id\", " +
            $"oauth_signature_method=\"HMAC-SHA256\", " +
            $"oauth_timestamp=\"{timestamp}\", " +
            $"oauth_nonce=\"testnonce{timestamp}\", " +
            $"oauth_version=\"1.0\", " +
            $"oauth_signature=\"invalid-signature\"");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1Response>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("error", result.Status);
        Assert.Equal("invalid_signature", result.Error);
    }

    #endregion

    #region Data Endpoint Test

    [Fact]
    public async Task Proxy_OAuth1_DataEndpoint_ReturnsData()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth1-test", "/oauth1/data");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<OAuth1DataResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.NotNull(result.Data);
    }

    #endregion

    #region Helper Classes and Options

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private record OAuth1Response(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("error")] string? Error
    );

    private record OAuth1EchoResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("oauth_params")] OAuthParamsDto? OAuthParams,
        [property: JsonPropertyName("request")] RequestInfoDto? Request
    );

    private record OAuthParamsDto(
        [property: JsonPropertyName("consumer_key")] string? ConsumerKey,
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("signature_method")] string? SignatureMethod,
        [property: JsonPropertyName("timestamp")] string? Timestamp,
        [property: JsonPropertyName("nonce")] string? Nonce,
        [property: JsonPropertyName("realm")] string? Realm,
        [property: JsonPropertyName("version")] string? Version
    );

    private record RequestInfoDto(
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("query_params")] JsonElement? QueryParams
    );

    private record OAuth1DataResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("data")] JsonElement? Data
    );

    #endregion
}
