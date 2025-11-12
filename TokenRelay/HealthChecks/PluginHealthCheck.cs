using Microsoft.Extensions.Diagnostics.HealthChecks;
using TokenRelay.Services;

namespace TokenRelay.HealthChecks;

public class PluginHealthCheck : IHealthCheck
{
    private readonly IPluginService _pluginService;
    private readonly ILogger<PluginHealthCheck> _logger;

    public PluginHealthCheck(IPluginService pluginService, ILogger<PluginHealthCheck> logger)
    {
        _pluginService = pluginService;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var plugins = _pluginService.GetLoadedPlugins();
            var healthyPlugins = plugins.Where(p => p.Status.Equals("loaded", StringComparison.OrdinalIgnoreCase)).ToList();
            var unhealthyPlugins = plugins.Where(p => !p.Status.Equals("loaded", StringComparison.OrdinalIgnoreCase)).ToList();

            var data = new Dictionary<string, object>
            {
                { "total_plugins", plugins.Count },
                { "healthy_plugins", healthyPlugins.Count },
                { "unhealthy_plugins", unhealthyPlugins.Count },
                { "plugin_names", plugins.Select(p => p.Name).ToList() }
            };

            if (unhealthyPlugins.Any())
            {
                var unhealthyNames = string.Join(", ", unhealthyPlugins.Select(p => p.Name));
                return Task.FromResult(HealthCheckResult.Degraded($"Some plugins are not healthy: {unhealthyNames}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy($"{healthyPlugins.Count} plugins loaded successfully", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Plugin health check failed: {ex.Message}"));
        }
    }
}
