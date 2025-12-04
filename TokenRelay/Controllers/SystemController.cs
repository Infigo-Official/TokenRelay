using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using TokenRelay.Models;
using TokenRelay.Services;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace TokenRelay.Controllers;

[ApiController]
public class SystemController : ControllerBase
{    
    private readonly IConfigurationService _configService;
    private readonly IPluginService _pluginService;
    private readonly ILogger<SystemController> _logger;
    private readonly IMemoryLogService _memoryLogService;
    private readonly ILogLevelService _logLevelService;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public SystemController(
        IConfigurationService configService, 
        IPluginService pluginService, 
        ILogger<SystemController> logger,
        IMemoryLogService memoryLogService,
        ILogLevelService logLevelService)
    {
        _configService = configService;
        _pluginService = pluginService;
        _logger = logger;
        _memoryLogService = memoryLogService;
        _logLevelService = logLevelService;
    }

    [HttpGet("status")]
    public ActionResult<StatusResponse> GetStatus()
    {
        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _logger.LogDebug("SystemController: Status request from {ClientIP}", clientIP);
        
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = GetApplicationVersion();
            var buildDate = GetBuildDate(assembly);
            var config = _configService.GetConfiguration();
            var proxyConfig = config.Proxy;

            var response = new StatusResponse
            {
                Version = version,
                BuildDate = buildDate,
                Plugins = _pluginService.GetLoadedPlugins(),
                ConfiguredTargets = GetEnhancedTargetInfo(proxyConfig),
                LogLevel = new LogLevelInfo
                {
                    Current = _logLevelService.GetCurrentLogLevelString(),
                    Configured = _logLevelService.GetConfiguredLogLevelString(),
                    IsOverridden = _logLevelService.IsLogLevelOverridden()
                },
                Permissions = proxyConfig.Permissions,
                TimeoutSeconds = proxyConfig.TimeoutSeconds,
                Mode = new ModeInfo
                {
                    Current = proxyConfig.Mode,
                    Chain = proxyConfig.Mode.Equals("chain", StringComparison.OrdinalIgnoreCase) 
                        ? new ChainInfo
                        {
                            Endpoint = proxyConfig.Chain.TargetProxy.Endpoint,
                            Description = proxyConfig.Chain.TargetProxy.Description,
                            HealthCheckUrl = proxyConfig.Chain.TargetProxy.HealthCheckUrl
                        }
                        : null
                }
            };

            _logger.LogInformation("SystemController: Status response sent - Version: {Version}, Plugins: {PluginCount}, Targets: {TargetCount} to {ClientIP}",
                version, response.Plugins.Count, response.ConfiguredTargets.Count, clientIP);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemController: Error generating status response for {ClientIP}", clientIP);
            return StatusCode(500, "Error retrieving system status");
        }
    }

    [HttpGet("logs")]
    public ActionResult<LogsResponse> GetLogs()
    {
        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _logger.LogDebug("SystemController: Logs retrieval request from {ClientIP}", clientIP);
        
        try
        {
            var config = _configService.GetConfiguration();
            if (!config.Proxy.Permissions.AllowLogRetrieval)
            {
                _logger.LogWarning("SystemController: Log retrieval denied - permission disabled for request from {ClientIP}", clientIP);
                return StatusCode(403, new { error = "Log retrieval is not enabled. Set 'Proxy.Permissions.AllowLogRetrieval' to true in configuration." });
            }

            _logger.LogInformation("SystemController: Retrieving system logs for {ClientIP}", clientIP);
            var logs = _memoryLogService.GetLogs();
            
            _logger.LogDebug("SystemController: Returning {LogCount} log entries to {ClientIP}", logs.TotalEntries, clientIP);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemController: Error retrieving system logs for {ClientIP}", clientIP);
            return StatusCode(500, new { error = "Failed to retrieve logs", message = ex.Message });
        }
    }

    [HttpPost("logs/level")]
    public ActionResult SetLogLevel([FromBody] SetLogLevelRequest request)
    {
        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _logger.LogDebug("SystemController: Log level change request to '{LogLevel}' from {ClientIP}", 
            request.LogLevel, clientIP);
        
        try
        {
            var config = _configService.GetConfiguration();
            if (!config.Proxy.Permissions.AllowLogLevelAdjustment)
            {
                _logger.LogWarning("SystemController: Log level adjustment denied - permission disabled for request from {ClientIP}", clientIP);
                return StatusCode(403, new { error = "Log level adjustment is not enabled. Set 'Proxy.Permissions.AllowLogLevelAdjustment' to true in configuration." });
            }

            if (string.IsNullOrEmpty(request.LogLevel))
            {
                _logger.LogWarning("SystemController: Empty log level provided from {ClientIP}", clientIP);
                return BadRequest(new { error = "LogLevel is required" });
            }

            if (!_logLevelService.IsValidLogLevel(request.LogLevel))
            {
                _logger.LogWarning("SystemController: Invalid log level '{LogLevel}' provided from {ClientIP}", 
                    request.LogLevel, clientIP);
                return BadRequest(new { 
                    error = "Invalid log level", 
                    validLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" } 
                });
            }

            if (!Enum.TryParse<LogLevel>(request.LogLevel, true, out var logLevel))
            {
                _logger.LogWarning("SystemController: Failed to parse log level '{LogLevel}' from {ClientIP}", 
                    request.LogLevel, clientIP);
                return BadRequest(new { error = "Invalid log level format" });
            }

            var previousLevel = _logLevelService.GetCurrentLogLevelString();
            _logLevelService.SetLogLevel(logLevel);

            _logger.LogInformation("SystemController: Log level changed from '{PreviousLevel}' to '{NewLevel}' by request from {ClientIP}", 
                previousLevel, request.LogLevel, clientIP);

            return Ok(new { 
                message = "Log level updated successfully", 
                previousLevel = previousLevel,
                newLevel = request.LogLevel,
                note = "Log level change is volatile and will revert to configuration on restart"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemController: Error setting log level to '{LogLevel}' from {ClientIP}", 
                request.LogLevel, clientIP);
            return StatusCode(500, new { error = "Failed to set log level", message = ex.Message });
        }
    }

    [HttpPost("config/targets/override")]
    public ActionResult OverrideTargetConfig([FromBody] TargetConfigOverrideRequest request)
    {
        try
        {
            var config = _configService.GetConfiguration();
            if (!config.Proxy.Permissions.AllowTargetConfigOverride)
            {
                return StatusCode(403, new { error = "Target configuration override is not enabled. Set 'Proxy.Permissions.AllowTargetConfigOverride' to true in configuration." });
            }

            if (request.Targets == null || !request.Targets.Any())
            {
                return BadRequest(new { error = "Targets dictionary is required and must not be empty" });
            }

            // Validate target configurations
            foreach (var kvp in request.Targets)
            {
                if (string.IsNullOrEmpty(kvp.Value.Endpoint))
                {
                    return BadRequest(new { error = $"Target '{kvp.Key}' must have a valid endpoint" });
                }
            }

            _configService.SetRuntimeTargetConfigOverrides(request.Targets);

            _logger.LogInformation("Target configuration overrides applied for {Count} targets: {Targets}", 
                request.Targets.Count, string.Join(", ", request.Targets.Keys));

            return Ok(new { 
                message = "Target configuration overrides applied successfully",
                overriddenTargets = request.Targets.Keys.ToList(),
                note = "Target configuration overrides are volatile and will revert to configuration on restart"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying target configuration overrides");
            return StatusCode(500, new { error = "Failed to apply target configuration overrides", message = ex.Message });
        }
    }

    [HttpDelete("config/targets/override")]
    public ActionResult ClearTargetConfigOverrides()
    {
        try
        {
            var overrides = _configService.GetRuntimeTargetConfigOverrides();
            var overriddenTargets = overrides.Keys.ToList();
            
            _configService.ClearRuntimeTargetConfigOverrides();

            _logger.LogInformation("Cleared target configuration overrides for {Count} targets", overriddenTargets.Count);

            return Ok(new { 
                message = "Target configuration overrides cleared successfully",
                clearedTargets = overriddenTargets
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing target configuration overrides");
            return StatusCode(500, new { error = "Failed to clear target configuration overrides", message = ex.Message });
        }
    }

    [HttpGet("config/targets/override")]
    public ActionResult<Dictionary<string, TargetConfig>> GetTargetConfigOverrides()
    {
        try
        {
            var overrides = _configService.GetRuntimeTargetConfigOverrides();
            return Ok(new { 
                overrides = overrides,
                count = overrides.Count,
                enabled = _configService.GetConfiguration().Proxy.Permissions.AllowTargetConfigOverride
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving target configuration overrides");
            return StatusCode(500, new { error = "Failed to retrieve target configuration overrides", message = ex.Message });
        }
    }    // Internal method that can be used by health checks

    internal string CheckConfiguration()
    {
        try
        {
            var config = _configService.GetConfiguration();
            if (!config?.Proxy?.Auth?.Tokens?.Any() ?? true)
                return "unhealthy";

            return "healthy";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration health check failed");
            return "unhealthy";
        }
    }

    private static DateTime GetBuildDate(Assembly assembly)
    {
        try
        {
            // Try to get build date from environment variable first (Docker)
            var buildDateEnv = Environment.GetEnvironmentVariable("BUILD_DATE");
            if (!string.IsNullOrEmpty(buildDateEnv) && DateTime.TryParse(buildDateEnv, out var buildDate))
            {
                return buildDate;
            }

            // Try to get from assembly metadata
            var attribute = assembly.GetCustomAttribute<AssemblyMetadataAttribute>();
            if (attribute?.Key == "BuildDate" && DateTime.TryParse(attribute.Value, out var assemblyBuildDate))
            {
                return assemblyBuildDate;
            }
        }
        catch
        {
            // Fallback to file creation time
        }

        // Fallback to current time if no build date is available
        return DateTime.UtcNow;
    }

    private static string GetApplicationVersion()
    {
        var hardcodedVersion = "4";

        /*
        // Try to get version from environment variables first (Docker/deployment)
        var dockerVersion = Environment.GetEnvironmentVariable("TOKENRELAY_VERSION");
        if (!string.IsNullOrEmpty(dockerVersion))
        {
            return dockerVersion;
        }

        // Try common CI/CD environment variables
        var buildVersion = Environment.GetEnvironmentVariable("BUILD_VERSION") ?? 
                          Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ??
                          Environment.GetEnvironmentVariable("CI_COMMIT_SHORT_SHA") ??
                          Environment.GetEnvironmentVariable("DOCKER_TAG");
        
        if (!string.IsNullOrEmpty(buildVersion))
        {
            return buildVersion;
        }*/

        // Try to get from assembly informational version (set via MSBuild)
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            return $"r{hardcodedVersion}.{informationalVersion}";
        }

        // Fall back to assembly version
        var assemblyVersion = assembly.GetName().Version?.ToString();
        if (!string.IsNullOrEmpty(assemblyVersion))
        {
            return $"r{hardcodedVersion}.{assemblyVersion}";
        }

        // Final fallback
        return $"r{hardcodedVersion}-unknown";
    }

    private List<TargetInfo> GetEnhancedTargetInfo(ProxyConfig proxyConfig)
    {
        var targetInfos = new List<TargetInfo>();
        var overrides = _configService.GetRuntimeTargetConfigOverrides();
        
        // First, add configured targets
        foreach (var kvp in proxyConfig.Targets)
        {
            var targetInfo = new TargetInfo
            {
                Key = kvp.Key,
                Description = kvp.Value.Description,
                Enabled = kvp.Value.Enabled,
                IsOverridden = overrides.ContainsKey(kvp.Key),
                IgnoreCertificateValidation = kvp.Value.IgnoreCertificateValidation
            };
            
            // Add endpoint and health check based on verbosity setting
            if (proxyConfig.StatusVerbosity != "none")
            {
                targetInfo.Endpoint = kvp.Value.Endpoint;
                targetInfo.HealthCheckUrl = kvp.Value.HealthCheckUrl;
                
                // Add headers if verbosity is set to headers
                if (proxyConfig.StatusVerbosity == "headers")
                {
                    targetInfo.Headers = kvp.Value.Headers;
                }
            }
            
            targetInfos.Add(targetInfo);
        }
        
        // Then, add or update with runtime overrides
        foreach (var kvp in overrides)
        {
            var existingTarget = targetInfos.FirstOrDefault(t => t.Key == kvp.Key);
            if (existingTarget != null)
            {
                // Update existing target with override information
                existingTarget.Description = kvp.Value.Description;
                existingTarget.Enabled = kvp.Value.Enabled;
                existingTarget.IsOverridden = true;
                existingTarget.IgnoreCertificateValidation = kvp.Value.IgnoreCertificateValidation;
                
                if (proxyConfig.StatusVerbosity != "none")
                {
                    existingTarget.Endpoint = kvp.Value.Endpoint;
                    existingTarget.HealthCheckUrl = kvp.Value.HealthCheckUrl;
                    
                    if (proxyConfig.StatusVerbosity == "headers")
                    {
                        existingTarget.Headers = kvp.Value.Headers;
                    }
                }
            }
            else
            {
                // Add new target from override
                var targetInfo = new TargetInfo
                {
                    Key = kvp.Key,
                    Description = kvp.Value.Description,
                    Enabled = kvp.Value.Enabled,
                    IsOverridden = true,
                    IgnoreCertificateValidation = kvp.Value.IgnoreCertificateValidation
                };
                
                if (proxyConfig.StatusVerbosity != "none")
                {
                    targetInfo.Endpoint = kvp.Value.Endpoint;
                    targetInfo.HealthCheckUrl = kvp.Value.HealthCheckUrl;
                    
                    if (proxyConfig.StatusVerbosity == "headers")
                    {
                        targetInfo.Headers = kvp.Value.Headers;
                    }
                }
                
                targetInfos.Add(targetInfo);
            }
        }
        
        return targetInfos.OrderBy(t => t.Key).ToList();
    }
}
