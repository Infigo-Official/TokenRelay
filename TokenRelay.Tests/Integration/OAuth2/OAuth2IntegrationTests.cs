using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TokenRelay.Tests.Integration.OAuth2;

/// <summary>
/// Integration tests for OAuth2 authentication through TokenRelay proxy.
///
/// These tests verify that:
/// 1. TokenRelay automatically acquires OAuth2 tokens when needed
/// 2. The acquired tokens are used for protected endpoint requests
/// 3. Different grant types (password, client_credentials) work correctly
/// 4. Token caching works (multiple requests use same token)
///
/// Prerequisites:
/// - Docker and docker-compose must be installed
/// - Port 5194 (TokenRelay) and 8192 (OAuth2 server) must be available
///
/// Running tests:
///   dotnet test --filter "Category=OAuth2Integration"
///
/// Manual container management (for debugging):
///   Set OAUTH2_SKIP_DOCKER=true environment variable
///   Start containers: docker-compose -f test/docker/docker-compose.oauth2-integration.yml up -d --build
/// </summary>
[Collection("OAuth2 Integration Tests")]
[Trait("Category", "Integration")]
[Trait("Category", "OAuth2Integration")]
public class OAuth2IntegrationTests
{
    private readonly OAuth2IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OAuth2IntegrationTests(OAuth2IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Password Grant Tests

    [Fact]
    public async Task Proxy_OAuth2PasswordGrant_AutomaticallyAcquiresTokenAndAccessesProtectedEndpoint()
    {
        // Arrange - Request to protected endpoint via proxy with password grant target
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/users");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should succeed because TokenRelay acquired a token automatically
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<UsersResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
        Assert.NotEmpty(result.Users);
        Assert.Equal("test_user", result.AuthenticatedAs);
    }

    [Fact]
    public async Task Proxy_OAuth2PasswordGrant_AccessesDataEndpoint()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/data");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<DataResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("OAuth authentication successful!", result.Message);
        Assert.True(result.TokenValid);
    }

    #endregion

    #region Client Credentials Grant Tests

    [Fact]
    public async Task Proxy_OAuth2ClientCredentials_AutomaticallyAcquiresTokenAndAccessesProtectedEndpoint()
    {
        // Arrange - Request to protected endpoint via proxy with client credentials target
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-client-credentials", "/v1/users");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should succeed with client_id as authenticated identity
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<UsersResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
        Assert.Equal("test_client_1", result.AuthenticatedAs);
    }

    [Fact]
    public async Task Proxy_OAuth2ClientCredentials_AccessesDataEndpoint()
    {
        // Arrange
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-client-credentials", "/v1/data");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<DataResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.TokenValid);
    }

    #endregion

    #region Token Caching Tests

    [Fact]
    public async Task Proxy_OAuth2_CachesTokenAcrossMultipleRequests()
    {
        // Arrange & Act - Make multiple requests
        var request1 = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/data");
        var response1 = await _fixture.HttpClient.SendAsync(request1);
        var content1 = await response1.Content.ReadAsStringAsync();

        var request2 = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/data");
        var response2 = await _fixture.HttpClient.SendAsync(request2);
        var content2 = await response2.Content.ReadAsStringAsync();

        var request3 = _fixture.CreateProxyRequest(HttpMethod.Get, "oauth2-password-grant", "/v1/users");
        var response3 = await _fixture.HttpClient.SendAsync(request3);
        var content3 = await response3.Content.ReadAsStringAsync();

        _output.WriteLine($"Response 1: {content1}");
        _output.WriteLine($"Response 2: {content2}");
        _output.WriteLine($"Response 3: {content3}");

        // Assert - All requests should succeed (token cached and reused)
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
    }

    #endregion

    #region Direct Server Tests (No Proxy)

    [Fact]
    public async Task MockOAuth2Server_Health_ReturnsOk()
    {
        // Arrange & Act
        var response = await _fixture.HttpClient.GetAsync($"{_fixture.OAuth2ServerBaseUrl}/health");
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Health Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MockOAuth2Server_ProtectedEndpoint_WithoutToken_Returns401()
    {
        // Arrange - Direct call to protected endpoint without token
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.OAuth2ServerBaseUrl}/v1/users");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var result = JsonSerializer.Deserialize<ErrorResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("unauthorized", result.Error);
    }

    [Fact]
    public async Task MockOAuth2Server_TokenEndpoint_PasswordGrant_ReturnsToken()
    {
        // Arrange - Direct call to token endpoint
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.OAuth2ServerBaseUrl}/v1/oauth/tokens");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", "test_client_1"),
            new KeyValuePair<string, string>("client_secret", "test_secret_1"),
            new KeyValuePair<string, string>("username", "test_user"),
            new KeyValuePair<string, string>("password", "test_password"),
            new KeyValuePair<string, string>("scope", "read write")
        });

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Token Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
        Assert.True(result.ExpiresIn > 0);
    }

    [Fact]
    public async Task MockOAuth2Server_TokenEndpoint_ClientCredentials_ReturnsToken()
    {
        // Arrange - Direct call to token endpoint with client credentials
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.OAuth2ServerBaseUrl}/v1/oauth/tokens");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", "test_client_1"),
            new KeyValuePair<string, string>("client_secret", "test_secret_1"),
            new KeyValuePair<string, string>("scope", "read")
        });

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Token Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
    }

    [Fact]
    public async Task MockOAuth2Server_TokenEndpoint_InvalidCredentials_Returns401()
    {
        // Arrange - Direct call with invalid credentials
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.OAuth2ServerBaseUrl}/v1/oauth/tokens");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", "invalid_client"),
            new KeyValuePair<string, string>("client_secret", "invalid_secret"),
            new KeyValuePair<string, string>("username", "test_user"),
            new KeyValuePair<string, string>("password", "test_password")
        });

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var result = JsonSerializer.Deserialize<ErrorResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("invalid_client", result.Error);
    }

    [Fact]
    public async Task MockOAuth2Server_ProtectedEndpoint_WithValidToken_ReturnsData()
    {
        // Arrange - First get a token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.OAuth2ServerBaseUrl}/v1/oauth/tokens");
        tokenRequest.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", "test_client_1"),
            new KeyValuePair<string, string>("client_secret", "test_secret_1")
        });

        var tokenResponse = await _fixture.HttpClient.SendAsync(tokenRequest);
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
        var tokenResult = JsonSerializer.Deserialize<TokenResponse>(tokenContent, JsonOptions);

        // Now use the token to access protected endpoint
        var dataRequest = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.OAuth2ServerBaseUrl}/v1/data");
        dataRequest.Headers.Add("Authorization", $"Bearer {tokenResult!.AccessToken}");

        // Act
        var response = await _fixture.HttpClient.SendAsync(dataRequest);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Data Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = JsonSerializer.Deserialize<DataResponse>(content, JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.TokenValid);
    }

    #endregion

    #region No Auth Target Tests

    [Fact]
    public async Task Proxy_NoAuthTarget_ProtectedEndpoint_Returns401()
    {
        // Arrange - Request to protected endpoint via no-auth target (no token added)
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "no-auth-target", "/v1/users");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Response Body: {content}");

        // Assert - Should fail because no token was added
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_NoAuthTarget_HealthEndpoint_ReturnsOk()
    {
        // Arrange - Health endpoint doesn't require auth
        var request = _fixture.CreateProxyRequest(HttpMethod.Get, "no-auth-target", "/health");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Helper Classes and Options

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

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

    private record DataResponse(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] JsonElement? Data,
        [property: JsonPropertyName("authenticated_as")] string? AuthenticatedAs,
        [property: JsonPropertyName("token_valid")] bool TokenValid
    );

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope
    );

    private record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription
    );

    #endregion
}
