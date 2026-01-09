using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace TokenRelay.Tests.Integration.OAuth2;

/// <summary>
/// Fixture for OAuth2 integration tests that manages Docker container lifecycle.
/// Uses docker-compose to start mock-oauth-server and tokenrelay containers.
///
/// Implements IAsyncLifetime for async setup/teardown of Docker containers.
///
/// Environment Variables:
///   OAUTH2_TOKENRELAY_PORT - Override TokenRelay port (default: 5194)
///   OAUTH2_SERVER_PORT - Override OAuth2 server port (default: 8192)
/// </summary>
public class OAuth2IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Base URL for the TokenRelay proxy service.
    /// Override with OAUTH2_TOKENRELAY_PORT environment variable.
    /// </summary>
    public string TokenRelayBaseUrl { get; }

    /// <summary>
    /// Base URL for the Mock OAuth2 Server (for direct testing).
    /// Override with OAUTH2_SERVER_PORT environment variable.
    /// </summary>
    public string OAuth2ServerBaseUrl { get; }

    /// <summary>
    /// Authentication token for TokenRelay proxy requests.
    /// </summary>
    public string AuthToken { get; } = "test-token-for-oauth2-integration";

    /// <summary>
    /// Shared HttpClient for making test requests.
    /// </summary>
    public HttpClient HttpClient { get; private set; } = null!;

    private readonly string _composeFilePath;
    private readonly string _projectName = "oauth2-integration";
    private bool _containersStarted;
    private bool _skipContainerManagement;

    public OAuth2IntegrationTestFixture()
    {
        // Allow skipping container management for manual testing
        _skipContainerManagement = Environment.GetEnvironmentVariable("OAUTH2_SKIP_DOCKER") == "true";

        // Allow port overrides for CI environments
        // Use 127.0.0.1 instead of localhost to avoid IPv6 resolution issues on Windows
        var tokenRelayPort = Environment.GetEnvironmentVariable("OAUTH2_TOKENRELAY_PORT") ?? "5194";
        var oauth2ServerPort = Environment.GetEnvironmentVariable("OAUTH2_SERVER_PORT") ?? "8192";
        TokenRelayBaseUrl = $"http://127.0.0.1:{tokenRelayPort}";
        OAuth2ServerBaseUrl = $"http://127.0.0.1:{oauth2ServerPort}";

        // Path relative to test execution directory
        // When running from TokenRelay.Tests/bin/Debug/net8.0, we need to go up to the repo root
        var baseDir = AppContext.BaseDirectory;
        _composeFilePath = Path.GetFullPath(
            Path.Combine(baseDir, "../../../../test/docker/docker-compose.oauth2-integration.yml"));

        // Alternative: if running from solution root
        if (!File.Exists(_composeFilePath))
        {
            _composeFilePath = Path.GetFullPath(
                Path.Combine(baseDir, "../../../../../test/docker/docker-compose.oauth2-integration.yml"));
        }
    }

    public async Task InitializeAsync()
    {
        if (_skipContainerManagement)
        {
            Console.WriteLine("OAUTH2_SKIP_DOCKER=true - Skipping Docker container startup");
            Console.WriteLine("Ensure containers are running manually with:");
            Console.WriteLine($"  docker-compose -f \"{_composeFilePath}\" up -d --build");
        }
        else
        {
            Console.WriteLine($"Starting OAuth2 integration test containers...");
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

        // Wait for services to be healthy
        await WaitForServiceHealthAsync(OAuth2ServerBaseUrl + "/health", "OAuth2 Server", TimeSpan.FromSeconds(60));
        await WaitForServiceHealthAsync(TokenRelayBaseUrl + "/health", "TokenRelay", TimeSpan.FromSeconds(90));

        Console.WriteLine("All services healthy. Ready for testing.");
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();

        if (_containersStarted && !_skipContainerManagement)
        {
            Console.WriteLine("Stopping OAuth2 integration test containers...");
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
    /// Creates an HTTP request with TokenRelay authentication headers.
    /// The target is specified via the TOKEN-RELAY-TARGET header, not in the URL path.
    /// </summary>
    public HttpRequestMessage CreateProxyRequest(HttpMethod method, string targetName, string path)
    {
        // URL format: /proxy/{path} where path is forwarded to the target endpoint
        // The target is identified by the TOKEN-RELAY-TARGET header
        var url = $"{TokenRelayBaseUrl}/proxy{path}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("TOKEN-RELAY-AUTH", AuthToken);
        request.Headers.Add("TOKEN-RELAY-TARGET", targetName);
        return request;
    }
}
