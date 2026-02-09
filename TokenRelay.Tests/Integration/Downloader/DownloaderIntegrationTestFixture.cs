using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TokenRelay.Tests.Integration.Downloader;

/// <summary>
/// Fixture for Downloader plugin integration tests that manages Docker container lifecycle.
/// Uses docker-compose to start TokenRelay with Downloader plugin and a mock file server:
///   Client -> TokenRelay (Downloader plugin) -> Mock File Server
///
/// Implements IAsyncLifetime for async setup/teardown of Docker containers.
///
/// Environment Variables:
///   DOWNLOADER_TOKENRELAY_PORT - Override TokenRelay port (default: 5198)
///   DOWNLOADER_SERVER_PORT - Override mock server port (default: 8196)
///   DOWNLOADER_SKIP_DOCKER - Skip Docker container management (for manual testing)
/// </summary>
public class DownloaderIntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Base URL for the TokenRelay proxy with Downloader plugin.
    /// Override with DOWNLOADER_TOKENRELAY_PORT environment variable.
    /// </summary>
    public string TokenRelayBaseUrl { get; }

    /// <summary>
    /// Base URL for the Mock File Server (for direct testing).
    /// Override with DOWNLOADER_SERVER_PORT environment variable.
    /// </summary>
    public string MockServerBaseUrl { get; }

    /// <summary>
    /// Docker-internal URL for the mock file server (used by the Downloader plugin inside Docker).
    /// </summary>
    public string MockServerInternalUrl { get; } = "http://mock-file-server:8080";

    /// <summary>
    /// Authentication token for the TokenRelay proxy.
    /// </summary>
    public string AuthToken { get; } = "test-token-for-downloader-integration";

    /// <summary>
    /// Shared HttpClient for making test requests.
    /// </summary>
    public HttpClient HttpClient { get; private set; } = null!;

    private readonly string _composeFilePath;
    private readonly string _projectName = "downloader-integration";
    private bool _containersStarted;
    private bool _skipContainerManagement;

    public DownloaderIntegrationTestFixture()
    {
        // Allow skipping container management for manual testing
        _skipContainerManagement = Environment.GetEnvironmentVariable("DOWNLOADER_SKIP_DOCKER") == "true";

        // Allow port overrides for CI environments
        var tokenRelayPort = Environment.GetEnvironmentVariable("DOWNLOADER_TOKENRELAY_PORT") ?? "5198";
        var serverPort = Environment.GetEnvironmentVariable("DOWNLOADER_SERVER_PORT") ?? "8196";

        // Use 127.0.0.1 instead of localhost to avoid IPv6 resolution issues on Windows
        TokenRelayBaseUrl = $"http://127.0.0.1:{tokenRelayPort}";
        MockServerBaseUrl = $"http://127.0.0.1:{serverPort}";

        // Path relative to test execution directory
        var baseDir = AppContext.BaseDirectory;
        _composeFilePath = Path.GetFullPath(
            Path.Combine(baseDir, "../../../../test/docker/docker-compose.downloader-integration.yml"));

        // Alternative: if running from solution root
        if (!File.Exists(_composeFilePath))
        {
            _composeFilePath = Path.GetFullPath(
                Path.Combine(baseDir, "../../../../../test/docker/docker-compose.downloader-integration.yml"));
        }
    }

    public async Task InitializeAsync()
    {
        if (_skipContainerManagement)
        {
            Console.WriteLine("DOWNLOADER_SKIP_DOCKER=true - Skipping Docker container startup");
            Console.WriteLine("Ensure containers are running manually with:");
            Console.WriteLine($"  docker-compose -f \"{_composeFilePath}\" up -d --build");
        }
        else
        {
            Console.WriteLine($"Starting Downloader integration test containers...");
            Console.WriteLine($"Docker Compose file: {_composeFilePath}");

            if (!File.Exists(_composeFilePath))
            {
                throw new FileNotFoundException(
                    $"Docker Compose file not found: {_composeFilePath}. " +
                    "Ensure you're running tests from the correct directory.");
            }

            // Clean up any existing containers from previous runs (may have been interrupted)
            Console.WriteLine("Cleaning up any existing containers...");
            try
            {
                await RunDockerComposeAsync("down", "-v");
            }
            catch
            {
                // Ignore errors from down - containers may not exist
            }

            // Start containers with build
            await RunDockerComposeAsync("up", "-d", "--build");
            _containersStarted = true;

            Console.WriteLine("Containers started. Waiting for services to be healthy...");
        }

        // Create HttpClient
        HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Wait for services to be healthy (in order: mock server -> tokenrelay)
        await WaitForServiceHealthAsync(MockServerBaseUrl + "/health", "Mock File Server", TimeSpan.FromSeconds(60));
        await WaitForServiceHealthAsync(TokenRelayBaseUrl + "/health", "TokenRelay", TimeSpan.FromSeconds(90));

        Console.WriteLine("All services healthy. Ready for Downloader testing.");
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();

        if (_containersStarted && !_skipContainerManagement)
        {
            Console.WriteLine("Stopping Downloader integration test containers...");
            try
            {
                await RunDockerComposeAsync("down", "-v");
                Console.WriteLine("Containers stopped and cleaned up.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to stop containers: {ex.Message}");
            }
        }
    }

    private async Task RunDockerComposeAsync(params string[] args)
    {
        var arguments = $"-p {_projectName} -f \"{_composeFilePath}\" {string.Join(" ", args)}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker-compose",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Console.WriteLine($"Running: docker-compose {arguments}");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine($"Output: {output}");
        }

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"docker-compose failed with exit code {process.ExitCode}.\n" +
                $"Arguments: {arguments}\n" +
                $"Error: {error}\n" +
                $"Output: {output}");
        }
    }

    private async Task WaitForServiceHealthAsync(string healthUrl, string serviceName, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        Console.WriteLine($"Waiting for {serviceName} at {healthUrl}...");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await HttpClient.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"{serviceName} is healthy after {stopwatch.Elapsed.TotalSeconds:F1}s");
                    return;
                }

                Console.WriteLine($"{serviceName} returned {response.StatusCode}, retrying...");
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Service not ready yet, continue waiting
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException(
            $"Service '{serviceName}' at {healthUrl} did not become healthy within {timeout.TotalSeconds}s. " +
            $"Last error: {lastException?.Message}");
    }

    /// <summary>
    /// Creates an HTTP POST request to execute a Downloader function with a URL parameter.
    /// Posts JSON body { "url": "..." } to /function/Downloader/{function}.
    /// </summary>
    public HttpRequestMessage CreateFunctionRequest(string url, string function = "fetch")
    {
        var requestUrl = $"{TokenRelayBaseUrl}/function/Downloader/{function}";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("TOKEN-RELAY-AUTH", AuthToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { url }),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    /// <summary>
    /// Creates an HTTP GET request to execute a Downloader function with url as query param.
    /// GET /function/Downloader/{function}?url=...
    /// </summary>
    public HttpRequestMessage CreateFunctionGetRequest(string url, string function = "fetch")
    {
        var encodedUrl = Uri.EscapeDataString(url);
        var requestUrl = $"{TokenRelayBaseUrl}/function/Downloader/{function}?url={encodedUrl}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add("TOKEN-RELAY-AUTH", AuthToken);
        return request;
    }
}
