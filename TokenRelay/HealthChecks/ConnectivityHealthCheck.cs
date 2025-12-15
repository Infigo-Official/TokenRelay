using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TokenRelay.Models;
using TokenRelay.Services;

namespace TokenRelay.HealthChecks;

public class ConnectivityHealthCheck : IHealthCheck
{
    private readonly IProxyService _proxyService;
    private readonly IConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectivityHealthCheck> _logger;
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    public ConnectivityHealthCheck(
        IProxyService proxyService,
        IConfigurationService configService,
        IHttpClientFactory httpClientFactory,
        ILogger<ConnectivityHealthCheck> logger)
    {
        _proxyService = proxyService;
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _configService.GetProxyConfig();

            var healthyTargets = new List<string>();
            var unhealthyTargets = new List<string>();
            var skippedTargets = new List<string>();
            var targetDetails = new List<object>();

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = HealthCheckTimeout;

            foreach (var target in config.Targets)
            {
                var healthConfig = target.Value.EffectiveHealthCheck;

                // Skip targets without health check configuration or disabled health checks
                if (healthConfig == null || string.IsNullOrWhiteSpace(healthConfig.Url))
                {
                    skippedTargets.Add($"{target.Key} (no health check configured)");
                    targetDetails.Add(new { name = target.Key, status = "skipped", type = (string?)null, reason = "No health check configured" });
                    continue;
                }

                if (!healthConfig.Enabled)
                {
                    skippedTargets.Add($"{target.Key} (health check disabled)");
                    targetDetails.Add(new { name = target.Key, status = "skipped", type = healthConfig.Type.ToString(), reason = "Health check disabled" });
                    continue;
                }

                try
                {
                    var (isHealthy, reason) = healthConfig.Type switch
                    {
                        HealthCheckType.TcpConnect => await CheckTcpConnectAsync(healthConfig.Url, cancellationToken),
                        HealthCheckType.HttpPost => await CheckHttpPostAsync(httpClient, healthConfig, cancellationToken),
                        HealthCheckType.HttpGet => await CheckHttpGetAsync(httpClient, healthConfig, cancellationToken),
                        _ => await CheckHttpGetAsync(httpClient, healthConfig, cancellationToken)
                    };

                    if (isHealthy)
                    {
                        healthyTargets.Add(target.Key);
                        targetDetails.Add(new { name = target.Key, status = "healthy", type = healthConfig.Type.ToString(), reason });
                    }
                    else
                    {
                        unhealthyTargets.Add($"{target.Key} ({reason})");
                        targetDetails.Add(new { name = target.Key, status = "unhealthy", type = healthConfig.Type.ToString(), reason });
                    }
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for target {Target} using {CheckType}",
                        target.Key, healthConfig.Type);
                    var errorReason = GetErrorReason(ex);
                    unhealthyTargets.Add($"{target.Key} ({errorReason})");
                    targetDetails.Add(new { name = target.Key, status = "unhealthy", type = healthConfig.Type.ToString(), reason = errorReason });
                }
            }

            var data = new Dictionary<string, object>
            {
                { "total_targets", config.Targets.Count },
                { "checked_targets", healthyTargets.Count + unhealthyTargets.Count },
                { "skipped_targets", skippedTargets.Count },
                { "healthy_targets", healthyTargets.Count },
                { "unhealthy_targets", unhealthyTargets.Count },
                { "target_details", targetDetails }
            };

            // If no targets have health checks configured
            if (skippedTargets.Count == config.Targets.Count)
            {
                return HealthCheckResult.Healthy("No health checks configured for targets", data: data);
            }

            if (unhealthyTargets.Any() && !healthyTargets.Any())
            {
                return HealthCheckResult.Unhealthy("All checked proxy targets are unreachable", data: data);
            }

            if (unhealthyTargets.Any())
            {
                var message = $"Some targets unreachable: {string.Join(", ", unhealthyTargets)}";
                if (skippedTargets.Any())
                {
                    message += $". Skipped: {string.Join(", ", skippedTargets)}";
                }
                return HealthCheckResult.Degraded(message, data: data);
            }

            var successMessage = $"All {healthyTargets.Count} checked targets are reachable";
            if (skippedTargets.Any())
            {
                successMessage += $". Skipped {skippedTargets.Count} targets without health checks";
            }
            return HealthCheckResult.Healthy(successMessage, data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connectivity health check failed");
            return HealthCheckResult.Unhealthy($"Connectivity check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs an HTTP GET request to check if the endpoint is healthy.
    /// Returns (isHealthy, reason) tuple with detailed status information.
    /// </summary>
    private async Task<(bool isHealthy, string reason)> CheckHttpGetAsync(HttpClient httpClient, HealthCheckConfig healthConfig, CancellationToken cancellationToken)
    {
        var testUrl = new Uri(healthConfig.Url).ToString();
        using var response = await httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        return GetHealthStatusWithReason(response, healthConfig);
    }

    /// <summary>
    /// Performs an HTTP POST request with optional body to check if the endpoint is healthy.
    /// Returns (isHealthy, reason) tuple with detailed status information.
    /// </summary>
    private async Task<(bool isHealthy, string reason)> CheckHttpPostAsync(HttpClient httpClient, HealthCheckConfig healthConfig, CancellationToken cancellationToken)
    {
        var testUrl = new Uri(healthConfig.Url).ToString();

        HttpContent? content = null;
        if (!string.IsNullOrEmpty(healthConfig.Body))
        {
            content = new StringContent(healthConfig.Body, System.Text.Encoding.UTF8, healthConfig.EffectiveContentType);
        }

        using var response = await httpClient.PostAsync(testUrl, content, cancellationToken);

        return GetHealthStatusWithReason(response, healthConfig);
    }

    /// <summary>
    /// Gets the health status and reason from an HTTP response.
    /// </summary>
    private static (bool isHealthy, string reason) GetHealthStatusWithReason(HttpResponseMessage response, HealthCheckConfig healthConfig)
    {
        var statusCode = (int)response.StatusCode;
        var reasonPhrase = response.ReasonPhrase ?? response.StatusCode.ToString();
        var reason = $"HTTP {statusCode} {reasonPhrase}";

        // 401 is always considered healthy (service is responding, just requires auth)
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (true, $"{reason} (auth required)");

        // Check against expected status codes
        var isHealthy = healthConfig.EffectiveExpectedStatusCodes.Contains(statusCode);
        return (isHealthy, reason);
    }

    /// <summary>
    /// Performs a TCP socket connection to check if the host:port is reachable.
    /// No HTTP request is made - just verifies network connectivity.
    /// Returns (isHealthy, reason) tuple with detailed status information.
    /// </summary>
    private async Task<(bool isHealthy, string reason)> CheckTcpConnectAsync(string url, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        var port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        using var tcpClient = new TcpClient();

        // Create a timeout cancellation token
        using var timeoutCts = new CancellationTokenSource(HealthCheckTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        await tcpClient.ConnectAsync(uri.Host, port, linkedCts.Token);
        return (tcpClient.Connected, "Connected successfully");
    }

    /// <summary>
    /// Extracts a user-friendly error reason from an exception.
    /// </summary>
    private static string GetErrorReason(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException or OperationCanceledException => "Connection timeout",
            System.Net.Sockets.SocketException socketEx => socketEx.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused => "Connection refused",
                System.Net.Sockets.SocketError.HostNotFound => "Host not found",
                System.Net.Sockets.SocketError.HostUnreachable => "Host unreachable",
                System.Net.Sockets.SocketError.NetworkUnreachable => "Network unreachable",
                System.Net.Sockets.SocketError.TimedOut => "Connection timeout",
                _ => $"Socket error: {socketEx.SocketErrorCode}"
            },
            HttpRequestException httpEx when httpEx.InnerException is System.Net.Sockets.SocketException innerSocket =>
                GetErrorReason(innerSocket),
            HttpRequestException httpEx => httpEx.Message,
            _ => ex.Message
        };
    }
}
