using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TokenRelay.Tests.Integration.Downloader;

/// <summary>
/// Integration tests for the Downloader plugin (file/image proxying).
///
/// These tests verify that:
/// 1. The Downloader plugin correctly fetches and streams remote files
/// 2. Content-Type headers are preserved from the remote server
/// 3. Various file types (text, JSON, binary, large) are handled correctly
/// 4. Error cases (missing params, invalid URLs, remote errors) return proper JSON errors
/// 5. Authentication is enforced (TOKEN-RELAY-AUTH required)
/// 6. Unsafe URL schemes (ftp://, file://) are rejected
///
/// Test Architecture:
///   Client -> TokenRelay (Downloader plugin) -> Mock File Server
///
/// Prerequisites:
/// - Docker and docker-compose must be installed
/// - Ports 5198, 8196 must be available
///
/// Running tests:
///   dotnet test --filter "Category=DownloaderIntegration"
///
/// Manual container management:
///   Set DOWNLOADER_SKIP_DOCKER=true environment variable
///   Start containers: docker-compose -f test/docker/docker-compose.downloader-integration.yml up -d --build
/// </summary>
[Collection("Downloader Integration Tests")]
[Trait("Category", "Integration")]
[Trait("Category", "DownloaderIntegration")]
public class DownloaderIntegrationTests
{
    private readonly DownloaderIntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DownloaderIntegrationTests(DownloaderIntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Happy Path Tests

    [Fact]
    public async Task Fetch_TextFile_ReturnsContentWithTextPlainContentType()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/sample.txt";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("Hello from the mock file server!", content);
    }

    [Fact]
    public async Task Fetch_JsonFile_ReturnsValidJsonWithApplicationJsonContentType()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/sample.json";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType?.ToString());

        // Verify it's valid JSON
        var json = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.Equal("Sample JSON file", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Fetch_BinaryFile_ReturnsPngWithCorrectMagicBytes()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/binary";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body Length: {bytes.Length} bytes");
        _output.WriteLine($"First 8 bytes: {BitConverter.ToString(bytes.Take(8).ToArray())}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("image/png", response.Content.Headers.ContentType?.ToString());

        // Verify PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(bytes.Length >= 8, "Response should contain at least PNG header");
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]); // P
        Assert.Equal(0x4E, bytes[2]); // N
        Assert.Equal(0x47, bytes[3]); // G
    }

    [Fact]
    public async Task Fetch_LargeFile_ReturnsFullContent()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/large";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body Length: {bytes.Length} bytes ({bytes.Length / 1024.0:F2} KB)");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1048576, bytes.Length); // Exactly 1MB
    }

    [Fact]
    public async Task Fetch_RedirectUrl_FollowsRedirectAndReturnsFinalContent()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/redirect";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body: {content}");

        // Assert - should follow redirect and return the sample.txt content
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello from the mock file server!", content);
    }

    [Fact]
    public async Task Fetch_ViaGetRequest_ReturnsContentSuccessfully()
    {
        // Arrange - use GET with query param instead of POST with JSON body
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/sample.txt";
        var request = _fixture.CreateFunctionGetRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello from the mock file server!", content);
    }

    #endregion

    #region Error Tests

    [Fact]
    public async Task Fetch_MissingUrlParameter_ReturnsJsonError()
    {
        // Arrange - POST with empty JSON body (no url param)
        var requestUrl = $"{_fixture.TokenRelayBaseUrl}/function/Downloader/fetch";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("TOKEN-RELAY-AUTH", _fixture.AuthToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert - should return JSON error (200 OK with success=false in body)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("url", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fetch_InvalidUrlFormat_ReturnsJsonError()
    {
        // Arrange
        var request = _fixture.CreateFunctionRequest("not-a-valid-url");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid URL", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fetch_Remote404_ReturnsJsonError()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/error/404";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("404", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Fetch_Remote500_ReturnsJsonError()
    {
        // Arrange
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/error/500";
        var request = _fixture.CreateFunctionRequest(fileUrl);

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("500", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Fetch_UnsupportedFunction_ReturnsError()
    {
        // Arrange - call a function that doesn't exist on Downloader
        var fileUrl = $"{_fixture.MockServerInternalUrl}/v1/files/sample.txt";
        var requestUrl = $"{_fixture.TokenRelayBaseUrl}/function/Downloader/nonexistent";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("TOKEN-RELAY-AUTH", _fixture.AuthToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { url = fileUrl }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert - should return error (either 404 or 200 with success=false)
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.False(result.GetProperty("success").GetBoolean());
        }
        else
        {
            // Plugin service wraps NotSupportedException as error dict, or controller returns 404
            Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                        response.StatusCode == HttpStatusCode.InternalServerError);
        }
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task Fetch_NoAuthHeader_Returns401()
    {
        // Arrange - request without TOKEN-RELAY-AUTH header
        var requestUrl = $"{_fixture.TokenRelayBaseUrl}/function/Downloader/fetch";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { url = $"{_fixture.MockServerInternalUrl}/v1/files/sample.txt" }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);

        _output.WriteLine($"Response Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fetch_InvalidAuthToken_Returns401()
    {
        // Arrange - request with wrong TOKEN-RELAY-AUTH token
        var requestUrl = $"{_fixture.TokenRelayBaseUrl}/function/Downloader/fetch";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("TOKEN-RELAY-AUTH", "wrong-token-value");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { url = $"{_fixture.MockServerInternalUrl}/v1/files/sample.txt" }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);

        _output.WriteLine($"Response Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fetch_FtpScheme_ReturnsJsonError()
    {
        // Arrange
        var request = _fixture.CreateFunctionRequest("ftp://example.com/file.txt");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("scheme", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fetch_FileScheme_ReturnsJsonError()
    {
        // Arrange
        var request = _fixture.CreateFunctionRequest("file:///etc/passwd");

        // Act
        var response = await _fixture.HttpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Response Status: {response.StatusCode}");
        _output.WriteLine($"Body: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("scheme", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
