using Microsoft.Extensions.Diagnostics.HealthChecks;
using TokenRelay.Services;

namespace TokenRelay.HealthChecks;

public class ConfigurationHealthCheck : IHealthCheck
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<ConfigurationHealthCheck> _logger;

    public ConfigurationHealthCheck(IConfigurationService configService, ILogger<ConfigurationHealthCheck> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _configService.GetConfiguration();
            
            if (config?.Proxy?.Auth?.Token == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Authentication token is not configured"));
            }

            var data = new Dictionary<string, object>
            {
                { "targets_count", config.Proxy.Targets?.Count ?? 0 },
                { "auth_configured", !string.IsNullOrEmpty(config.Proxy.Auth.Token) },
                { "proxy_mode", config.Proxy.Mode ?? "direct" }
            };

            // Add chain configuration info if in chain mode
            if (!string.IsNullOrEmpty(config.Proxy.Mode) && 
                config.Proxy.Mode.Equals("chain", StringComparison.OrdinalIgnoreCase))
            {
                data.Add("chain_endpoint", config.Proxy.Chain?.TargetProxy?.Endpoint ?? "not configured");
                data.Add("chain_configured", !string.IsNullOrEmpty(config.Proxy.Chain?.TargetProxy?.Endpoint));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Configuration is valid", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Configuration check failed: {ex.Message}"));
        }
    }
}
