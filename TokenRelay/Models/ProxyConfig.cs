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
    public string? HealthCheckUrl { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool Enabled { get; set; } = true;

    // Optional token for downstream proxy authentication - used when chaining proxies
    public string Token { get; set; } = string.Empty;
    
    // Allow ignoring SSL certificate validation errors (e.g., self-signed certificates)
    public bool IgnoreCertificateValidation { get; set; } = false;
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
