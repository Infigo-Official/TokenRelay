using Microsoft.Extensions.Diagnostics.HealthChecks;
using TokenRelay.Services;

namespace TokenRelay.HealthChecks;

public class ConnectivityHealthCheck : IHealthCheck
{
    private readonly IProxyService _proxyService;
    private readonly IConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectivityHealthCheck> _logger;

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

            using var httpClient = _httpClientFactory.CreateClient();
            // Quick health check timeout of 5 second to avoid long delays in the health check
            httpClient.Timeout = TimeSpan.FromSeconds(5); 

            foreach (var target in config.Targets)
            {
                // Skip targets without health check URL
                if (string.IsNullOrWhiteSpace(target.Value.HealthCheckUrl))
                {
                    skippedTargets.Add($"{target.Key} (no health check URL configured)");
                    continue;
                }

                try
                {
                    var testUrl = new Uri(target.Value.HealthCheckUrl).ToString();
                    using var response = await httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        // Consider 401 as healthy since the service is responding
                        healthyTargets.Add(target.Key);
                    }
                    else
                    {
                        unhealthyTargets.Add($"{target.Key} ({response.StatusCode})");
                    }
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for target {Target}", target.Key);
                    unhealthyTargets.Add($"{target.Key} (connection failed)");
                }
            }
            
            var data = new Dictionary<string, object>
            {
                { "total_targets", config.Targets.Count },
                { "checked_targets", healthyTargets.Count + unhealthyTargets.Count },
                { "skipped_targets", skippedTargets.Count },
                { "healthy_targets", healthyTargets.Count },
                { "unhealthy_targets", unhealthyTargets.Count }
            };

            // If no targets have health check URLs configured
            if (skippedTargets.Count == config.Targets.Count)
            {
                return HealthCheckResult.Healthy("No health check URLs configured for targets", data: data);
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
                successMessage += $". Skipped {skippedTargets.Count} targets without health check URLs";
            }
            return HealthCheckResult.Healthy(successMessage, data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connectivity health check failed");
            return HealthCheckResult.Unhealthy($"Connectivity check failed: {ex.Message}");
        }
    }
}
