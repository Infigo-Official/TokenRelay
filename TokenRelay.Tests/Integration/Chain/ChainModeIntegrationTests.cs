using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TokenRelay.Tests.Integration.Chain;

/// <summary>
/// Integration tests for Chain Mode (proxy-to-proxy forwarding).
///
/// These tests verify that:
/// 1. Requests are correctly forwarded from upstream to downstream proxy
/// 2. TOKEN-RELAY-TARGET header is preserved through the chain
/// 3. Request bodies are correctly forwarded through the chain
/// 4. OAuth authentication works through the chain
/// 5. Static auth headers are correctly injected by downstream proxy
///
/// Test Architecture:
///   Client -> Upstream (chain mode) -> Downstream (direct mode) -> Mock Server
///
/// Prerequisites:
/// - Docker and docker-compose must be installed
/// - Ports 5196, 5197, 8195 must be available
///
/// Running tests:
///   dotnet test --filter "Category=ChainIntegration"
///
/// Manual container management:
///   Set CHAIN_SKIP_DOCKER=true environment variable
///   Start containers: docker-compose -f test/docker/docker-compose.chain-integration.yml up -d --build
/// </summary>
[Collection("Chain Mode Integration Tests")]
[Trait("Category", "Integration")]
[Trait("Category", "ChainIntegration")]
public class ChainModeIntegrationTests
{
    private readonly ChainModeIntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ChainModeIntegrationTests(ChainModeIntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Basic Chain Forwarding Tests

    [Fact]
    public async Task ChainMode_ForwardsRequestToDownstreamProxy()
    {
        // Arrange - Request through upstream proxy (chain mode)
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should succeed, proving request was forwarded through chain
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.Equal("GET", result.Method);
    }

    [Fact]
    public async Task ChainMode_PreservesTargetHeader_DownstreamReceivesCorrectTarget()
    {
        // Arrange - Request OAuth2 target through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should succeed with OAuth2 token (proving TARGET header was preserved)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        // If OAuth2 worked, the Authorization header would be present (Bearer token)
        Assert.NotNull(result.AuthorizationReceived);
        Assert.StartsWith("Bearer ", result.AuthorizationReceived);
    }

    #endregion

    #region Request Body Forwarding Tests

    [Fact]
    public async Task ChainMode_ForwardsRequestBody_POST()
    {
        // Arrange - POST with JSON body through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Post, "no-auth-target", "/v1/echo");
        var bodyContent = new { message = "Hello through chain", value = 42 };
        request.Content = new StringContent(
            JsonSerializer.Serialize(bodyContent),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Body should be preserved through chain
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("POST", result.Method);
        Assert.NotNull(result.Body);
        Assert.Contains("Hello through chain", result.Body);
        Assert.Contains("42", result.Body);
    }

    [Fact]
    public async Task ChainMode_ForwardsRequestBody_PUT()
    {
        // Arrange - PUT with JSON body through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Put, "no-auth-target", "/v1/echo");
        var bodyContent = new { id = 123, name = "updated via chain" };
        request.Content = new StringContent(
            JsonSerializer.Serialize(bodyContent),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("PUT", result.Method);
        Assert.NotNull(result.Body);
        Assert.Contains("updated via chain", result.Body);
    }

    #endregion

    #region OAuth Through Chain Tests

    [Fact]
    public async Task ChainMode_OAuth2Target_AcquiresTokenAndAccessesProtectedEndpoint()
    {
        // Arrange - Request protected endpoint through chain with OAuth2 target
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/users");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should succeed (downstream proxy acquired OAuth2 token)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<UsersResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
        Assert.Equal("test_user", result.AuthenticatedAs);
    }

    #endregion

    #region Static Auth Through Chain Tests

    [Fact]
    public async Task ChainMode_StaticAuthTarget_InjectsHeaders()
    {
        // Arrange - Request static auth target through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "static-auth-target", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Static auth headers should be injected by downstream
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("Bearer chain-static-token-12345", result.AuthorizationReceived);
    }

    #endregion

    #region High Priority Chain Mode Tests

    /// <summary>
    /// HIGH PRIORITY: Test large request body is buffered correctly in chain mode.
    /// Code Location: ProxyService.cs:464
    /// </summary>
    [Fact]
    public async Task ChainMode_LargeRequestBody_BufferedCorrectly()
    {
        // Arrange - Large JSON body (500KB) through chain
        var largeData = new
        {
            description = "Large chain mode payload test",
            data = new string('Z', 512 * 1024) // 512KB of data
        };
        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(largeData);

        var request = _fixture.CreateChainProxyRequest(HttpMethod.Post, "no-auth-target", "/v1/echo");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _output.WriteLine($"Sending {jsonPayload.Length} bytes ({jsonPayload.Length / 1024.0:F2} KB) through chain");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Length: {content.Length} bytes");

        // Assert - Large body should be buffered and forwarded through chain
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Body);
        Assert.Contains("Large chain mode payload test", result.Body);
    }

    /// <summary>
    /// HIGH PRIORITY: Test that query parameters are preserved through chain.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_QueryParams_PreservedThroughChain()
    {
        // Arrange - Request with query params through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/echo?param1=value1&param2=value2");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.QueryParams);
        Assert.Equal("value1", result.QueryParams["param1"]);
        Assert.Equal("value2", result.QueryParams["param2"]);
    }

    /// <summary>
    /// HIGH PRIORITY: Test that client headers are preserved through chain.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_ClientHeaders_PreservedThroughChain()
    {
        // Arrange - Request with custom headers through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/echo");
        request.Headers.Add("X-Custom-Client-Header", "custom-value-123");
        request.Headers.Add("X-Request-Id", "chain-test-request-id");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Headers);
        // Custom headers should be preserved through the chain
        Assert.True(result.Headers.ContainsKey("X-Custom-Client-Header") || result.Headers.ContainsKey("x-custom-client-header"));
    }

    /// <summary>
    /// HIGH PRIORITY: Test all HTTP methods work through chain.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_DELETE_Request_WorksCorrectly()
    {
        // Arrange - DELETE request through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Delete, "no-auth-target", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("DELETE", result.Method);
    }

    /// <summary>
    /// HIGH PRIORITY: Test PATCH method through chain.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_PATCH_Request_WorksCorrectly()
    {
        // Arrange - PATCH request with body through chain
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Patch, "no-auth-target", "/v1/echo");
        var bodyContent = new { status = "patched", via = "chain" };
        request.Content = new StringContent(
            JsonSerializer.Serialize(bodyContent),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("PATCH", result.Method);
        Assert.NotNull(result.Body);
        Assert.Contains("patched", result.Body);
    }

    #endregion

    #region High Priority - Chain Mode Error Handling

    /// <summary>
    /// HIGH PRIORITY: Test that unknown target returns error in chain mode.
    /// Code Location: ProxyService.cs:352-355
    /// </summary>
    [Fact]
    public async Task ChainMode_UnknownTarget_ReturnsError()
    {
        // Arrange - Request with non-existent target
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "non-existent-target", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should return error for unknown target
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected error status for unknown target, got {response.StatusCode}");
    }

    /// <summary>
    /// HIGH PRIORITY: Test concurrent requests through chain mode.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_ConcurrentRequests_AllSucceed()
    {
        // Arrange - Multiple concurrent requests through chain
        var tasks = new Task<HttpResponseMessage>[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "no-auth-target", $"/v1/echo?i={i}");
            tasks[i] = _fixture.HttpClient.SendAsync(request);
        }

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert - All should succeed
        _output.WriteLine($"Concurrent chain requests: {responses.Length}");
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>
    /// HIGH PRIORITY: Test error status code passthrough in chain mode.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_TargetError_PassesThrough()
    {
        // Arrange - Request to status endpoint that returns 404
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/status/404");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Error should be passed through the chain
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Medium Priority - Chain Mode Content Types

    /// <summary>
    /// MEDIUM PRIORITY: Test various content types through chain.
    /// Code Location: ProxyService.cs:337-559
    /// </summary>
    [Fact]
    public async Task ChainMode_JsonContent_PreservedThroughChain()
    {
        // Arrange
        var request = _fixture.CreateChainProxyRequest(HttpMethod.Post, "no-auth-target", "/v1/echo");
        var jsonContent = new { key = "value", nested = new { a = 1, b = 2 } };
        request.Content = new StringContent(
            JsonSerializer.Serialize(jsonContent),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.ContentType);
        Assert.Contains("application/json", result.ContentType);
    }

    #endregion

    #region Downstream Direct Access Tests (Validation)

    [Fact]
    public async Task Downstream_DirectAccess_WorksIndependently()
    {
        // Arrange - Direct request to downstream (bypassing upstream)
        var request = _fixture.CreateDownstreamProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/echo");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Downstream should work independently
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<EchoResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
    }

    #endregion

    #region Helper Classes and Options

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private record EchoResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("headers")] Dictionary<string, string>? Headers,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("content_type")] string? ContentType,
        [property: JsonPropertyName("content_length")] int? ContentLength,
        [property: JsonPropertyName("authorization_received")] string? AuthorizationReceived,
        [property: JsonPropertyName("token_relay_auth_present")] bool TokenRelayAuthPresent,
        [property: JsonPropertyName("token_relay_target_present")] bool TokenRelayTargetPresent,
        [property: JsonPropertyName("query_params")] Dictionary<string, string>? QueryParams,
        [property: JsonPropertyName("timestamp")] double? Timestamp
    );

    private record UsersResponse(
        [property: JsonPropertyName("users")] UserDto[]? Users,
        [property: JsonPropertyName("authenticated_as")] string? AuthenticatedAs,
        [property: JsonPropertyName("scope")] string? Scope
    );

    private record UserDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("email")] string? Email
    );

    #endregion
}
