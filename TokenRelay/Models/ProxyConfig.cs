using System.Text.Json.Serialization;

namespace TokenRelay.Models;

public class ProxyConfig
{
    public ProxyAuthConfig Auth { get; set; } = new();
    public string Mode { get; set; } = "direct";
    public ChainConfig Chain { get; set; } = new();
    public Dictionary<string, TargetConfig> Targets { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 300; // Default 5 minutes
    public int LogBufferMinutes { get; set; } = 20; // Keep logs in memory for X minutes
    public ProxyPermissionsConfig Permissions { get; set; } = new();
    public string StatusVerbosity { get; set; } = "endpoints"; // "none", "endpoints", "headers"
}

public class ProxyAuthConfig
{
    public List<string> Tokens { get; set; } = new();
    public string EncryptionKey { get; set; } = string.Empty;
    
    // Backward compatibility
    public string Token 
    { 
        get => Tokens.FirstOrDefault() ?? string.Empty;
        set 
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (Tokens.Count == 0)
                    Tokens.Add(value);
                else
                    Tokens[0] = value;
            }
        }
    }
}

public class ProxyPermissionsConfig
{
    public bool AllowTargetConfigOverride { get; set; } = false; // Runtime target config override
    public bool AllowLogRetrieval { get; set; } = true; // Allow retrieving logs via API
    public bool AllowLogLevelAdjustment { get; set; } = true; // Allow changing log level at runtime
}

public class TargetConfig
{
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Legacy health check URL property. Use HealthCheck for new configurations.
    /// Kept for backward compatibility.
    /// </summary>
    public string? HealthCheckUrl { get; set; }

    /// <summary>
    /// New structured health check configuration with type support.
    /// Takes precedence over HealthCheckUrl when both are specified.
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }

    /// <summary>
    /// Gets the effective health check configuration, resolving backward compatibility.
    /// Returns HealthCheck if set, otherwise creates config from HealthCheckUrl.
    /// </summary>
    [JsonIgnore]
    public HealthCheckConfig? EffectiveHealthCheck
    {
        get
        {
            // New HealthCheck property takes precedence
            if (HealthCheck != null)
                return HealthCheck;

            // Fall back to legacy HealthCheckUrl
            if (!string.IsNullOrWhiteSpace(HealthCheckUrl))
                return new HealthCheckConfig
                {
                    Url = HealthCheckUrl,
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                };

            return null;
        }
    }

    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool Enabled { get; set; } = true;

    // Optional token for downstream proxy authentication - used when chaining proxies
    public string Token { get; set; } = string.Empty;

    // Allow ignoring SSL certificate validation errors (e.g., self-signed certificates)
    public bool IgnoreCertificateValidation { get; set; } = false;

    // Authentication type: "static" or "oauth"
    public string AuthType { get; set; } = "static";

    // OAuth authentication data (generic key-value pairs)
    public Dictionary<string, string> AuthData { get; set; } = new();

    /// <summary>
    /// Variables used for placeholder substitution in query strings ({name}) and JSON bodies ({{name}}).
    /// Useful for NetSuite script/deploy params, API versioning, etc.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Backward compatibility: "queryParams" in config JSON merges into Variables via TryAdd.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> QueryParams
    {
        set
        {
            if (value != null)
            {
                foreach (var kvp in value)
                    Variables.TryAdd(kvp.Key, kvp.Value);
            }
        }
    }
}

/// <summary>
/// Health check configuration for target endpoints.
/// </summary>
public class HealthCheckConfig
{
    /// <summary>
    /// The URL to check. Can be absolute or relative (relative URLs are resolved against the target endpoint).
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Whether the health check is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The type of health check to perform. Default is HttpGet.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HealthCheckType Type { get; set; } = HealthCheckType.HttpGet;

    /// <summary>
    /// Optional request body for HttpPost health checks.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Content-Type header for HttpPost health checks.
    /// Default is "application/json".
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets the effective content type, defaulting to "application/json" if not specified.
    /// </summary>
    [JsonIgnore]
    public string EffectiveContentType =>
        string.IsNullOrWhiteSpace(ContentType) ? "application/json" : ContentType;

    /// <summary>
    /// Expected HTTP status codes that indicate a healthy response.
    /// Default is [200]. Can specify multiple codes like [200, 201, 202].
    /// Note: 401 is always considered healthy (service is responding).
    /// </summary>
    public List<int>? ExpectedStatusCodes { get; set; }

    /// <summary>
    /// Gets the effective expected status codes, defaulting to [200] if not specified.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> EffectiveExpectedStatusCodes =>
        ExpectedStatusCodes is { Count: > 0 } ? ExpectedStatusCodes : [200];
}

/// <summary>
/// Types of health checks supported.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthCheckType
{
    /// <summary>
    /// Performs an HTTP GET request and validates the response status code.
    /// Success: Expected status codes (default 200) or 401.
    /// </summary>
    HttpGet = 0,

    /// <summary>
    /// Opens a TCP socket connection to verify the host:port is reachable.
    /// No HTTP request is made - just verifies network connectivity.
    /// </summary>
    TcpConnect = 1,

    /// <summary>
    /// Performs an HTTP POST request with optional body and validates the response status code.
    /// Success: Expected status codes (default 200) or 401.
    /// </summary>
    HttpPost = 2
}

public class PluginConfig
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
}

public class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string[] Destinations { get; set; } = ["console"];
}

public class TokenRelayConfig
{
    public ProxyConfig Proxy { get; set; } = new();
    public Dictionary<string, PluginConfig> Plugins { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public class ChainConfig
{
    public TargetConfig TargetProxy { get; set; } = new();
}
