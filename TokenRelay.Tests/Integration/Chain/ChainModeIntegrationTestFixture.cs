using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace TokenRelay.Tests.Integration.Chain;

/// <summary>
/// Fixture for Chain Mode integration tests that manages Docker container lifecycle.
/// Uses docker-compose to start two TokenRelay instances in chain configuration:
///   Client -> TokenRelay-Upstream (chain mode) -> TokenRelay-Downstream (direct mode) -> Mock Server
///
/// Implements IAsyncLifetime for async setup/teardown of Docker containers.
///
/// Environment Variables:
///   CHAIN_UPSTREAM_PORT - Override upstream TokenRelay port (default: 5196)
///   CHAIN_DOWNSTREAM_PORT - Override downstream TokenRelay port (default: 5197)
///   CHAIN_SERVER_PORT - Override mock server port (default: 8195)
///   CHAIN_SKIP_DOCKER - Skip Docker container management (for manual testing)
/// </summary>
public class ChainModeIntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Base URL for the Upstream TokenRelay proxy (chain mode - entry point).
    /// Override with CHAIN_UPSTREAM_PORT environment variable.
    /// </summary>
    public string UpstreamBaseUrl { get; }

    /// <summary>
    /// Base URL for the Downstream TokenRelay proxy (direct mode - connects to mock server).
    /// Override with CHAIN_DOWNSTREAM_PORT environment variable.
    /// </summary>
    public string DownstreamBaseUrl { get; }

    /// <summary>
    /// Base URL for the Mock OAuth2 Server (for direct testing).
    /// Override with CHAIN_SERVER_PORT environment variable.
    /// </summary>
    public string MockServerBaseUrl { get; }

    /// <summary>
    /// Authentication token for the Upstream TokenRelay proxy.
    /// </summary>
    public string UpstreamAuthToken { get; } = "test-token-for-chain-integration";

    /// <summary>
    /// Authentication token for the Downstream TokenRelay proxy.
    /// </summary>
    public string DownstreamAuthToken { get; } = "test-token-for-chain-downstream";

    /// <summary>
    /// Shared HttpClient for making test requests.
    /// </summary>
    public HttpClient HttpClient { get; private set; } = null!;

    private readonly string _composeFilePath;
    private readonly string _projectName = "chain-integration";
    private bool _containersStarted;
    private bool _skipContainerManagement;

    public ChainModeIntegrationTestFixture()
    {
        // Allow skipping container management for manual testing
        _skipContainerManagement = Environment.GetEnvironmentVariable("CHAIN_SKIP_DOCKER") == "true";

        // Allow port overrides for CI environments
        var upstreamPort = Environment.GetEnvironmentVariable("CHAIN_UPSTREAM_PORT") ?? "5196";
        var downstreamPort = Environment.GetEnvironmentVariable("CHAIN_DOWNSTREAM_PORT") ?? "5197";
        var serverPort = Environment.GetEnvironmentVariable("CHAIN_SERVER_PORT") ?? "8195";

        // Use 127.0.0.1 instead of localhost to avoid IPv6 resolution issues on Windows
        UpstreamBaseUrl = $"http://127.0.0.1:{upstreamPort}";
        DownstreamBaseUrl = $"http://127.0.0.1:{downstreamPort}";
        MockServerBaseUrl = $"http://127.0.0.1:{serverPort}";

        // Path relative to test execution directory
        var baseDir = AppContext.BaseDirectory;
        _composeFilePath = Path.GetFullPath(
            Path.Combine(baseDir, "../../../../test/docker/docker-compose.chain-integration.yml"));

        // Alternative: if running from solution root
        if (!File.Exists(_composeFilePath))
        {
            _composeFilePath = Path.GetFullPath(
                Path.Combine(baseDir, "../../../../../test/docker/docker-compose.chain-integration.yml"));
        }
    }

    public async Task InitializeAsync()
    {
        if (_skipContainerManagement)
        {
            Console.WriteLine("CHAIN_SKIP_DOCKER=true - Skipping Docker container startup");
            Console.WriteLine("Ensure containers are running manually with:");
            Console.WriteLine($"  docker-compose -f \"{_composeFilePath}\" up -d --build");
        }
        else
        {
            Console.WriteLine($"Starting Chain Mode integration test containers...");
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

        // Wait for services to be healthy (in order: mock server -> downstream -> upstream)
        await WaitForServiceHealthAsync(MockServerBaseUrl + "/health", "Mock Server", TimeSpan.FromSeconds(60));
        await WaitForServiceHealthAsync(DownstreamBaseUrl + "/health", "Downstream TokenRelay", TimeSpan.FromSeconds(90));
        await WaitForServiceHealthAsync(UpstreamBaseUrl + "/health", "Upstream TokenRelay", TimeSpan.FromSeconds(90));

        Console.WriteLine("All services healthy. Ready for Chain Mode testing.");
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();

        if (_containersStarted && !_skipContainerManagement)
        {
            Console.WriteLine("Stopping Chain Mode integration test containers...");
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
    /// Creates an HTTP request to the Upstream proxy with TokenRelay authentication headers.
    /// The request will be forwarded through the chain: Upstream -> Downstream -> Target.
    /// </summary>
    public HttpRequestMessage CreateChainProxyRequest(HttpMethod method, string targetName, string path)
    {
        var url = $"{UpstreamBaseUrl}/proxy{path}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("TOKEN-RELAY-AUTH", UpstreamAuthToken);
        request.Headers.Add("TOKEN-RELAY-TARGET", targetName);
        return request;
    }

    /// <summary>
    /// Creates an HTTP request directly to the Downstream proxy (bypassing upstream).
    /// Used for comparison testing and validating downstream works independently.
    /// </summary>
    public HttpRequestMessage CreateDownstreamProxyRequest(HttpMethod method, string targetName, string path)
    {
        var url = $"{DownstreamBaseUrl}/proxy{path}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("TOKEN-RELAY-AUTH", DownstreamAuthToken);
        request.Headers.Add("TOKEN-RELAY-TARGET", targetName);
        return request;
    }
}
