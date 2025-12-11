namespace TokenRelay.Models;

public class HealthResponse
{
    public string Status { get; set; } = "healthy";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Uptime { get; set; } = string.Empty;
    public Dictionary<string, string> Checks { get; set; } = new();
}

public class StatusResponse
{
    public string Version { get; set; } = string.Empty;
    public DateTime BuildDate { get; set; }
    public List<PluginInfo> Plugins { get; set; } = new();
    public List<TargetInfo> ConfiguredTargets { get; set; } = new();
    public LogLevelInfo LogLevel { get; set; } = new();
    public ProxyPermissionsConfig Permissions { get; set; } = new();
    public int TimeoutSeconds { get; set; }
    public ModeInfo Mode { get; set; } = new();
}

public class TargetInfo
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsOverridden { get; set; } = false;
    public string? Endpoint { get; set; }
    /// <summary>
    /// Legacy health check URL. Use HealthCheck for full configuration details.
    /// </summary>
    public string? HealthCheckUrl { get; set; }
    /// <summary>
    /// Full health check configuration including type (HttpGet, TcpConnect).
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public bool IgnoreCertificateValidation { get; set; } = false;
}

public class LogLevelInfo
{
    public string Current { get; set; } = string.Empty;
    public string Configured { get; set; } = string.Empty;
    public bool IsOverridden { get; set; } = false;
}

public class ModeInfo
{
    public string Current { get; set; } = string.Empty;
    public ChainInfo? Chain { get; set; }
}

public class ChainInfo
{
    public string Endpoint { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Legacy health check URL. Use HealthCheck for full configuration details.
    /// </summary>
    public string? HealthCheckUrl { get; set; }
    /// <summary>
    /// Full health check configuration including type (HttpGet, TcpConnect).
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }
}

public class PluginInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}

public class LogsResponse
{
    public string CurrentLogLevel { get; set; } = string.Empty;
    public int BufferMinutes { get; set; }
    public List<LogEntry> Logs { get; set; } = new();
    public int TotalEntries { get; set; }
}

public class SetLogLevelRequest
{
    public string LogLevel { get; set; } = string.Empty;
}

public class TargetConfigOverrideRequest
{
    public Dictionary<string, TargetConfig> Targets { get; set; } = new();
}
