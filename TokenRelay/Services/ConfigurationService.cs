using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using TokenRelay.Models;
using System;

namespace TokenRelay.Services;

public interface IConfigurationService
{
    TokenRelayConfig GetConfiguration();
    ProxyConfig GetProxyConfig();
    TargetConfig? GetTargetConfig(string targetName);
    bool ValidateAuthToken(string token);
    Task ReloadConfigurationAsync();
    void SetRuntimeTargetConfigOverrides(Dictionary<string, TargetConfig> overrides);
    Dictionary<string, TargetConfig> GetRuntimeTargetConfigOverrides();
    void ClearRuntimeTargetConfigOverrides();
}

public class ConfigurationService : IConfigurationService
{
    private readonly IOptionsMonitor<TokenRelayConfig> _configMonitor;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _configPath;
    private readonly Dictionary<string, TargetConfig> _runtimeTargetOverrides = new();

    public ConfigurationService(
        IOptionsMonitor<TokenRelayConfig> configMonitor,
        ILogger<ConfigurationService> logger,
        IConfiguration configuration)
    {
        _configMonitor = configMonitor;
        _logger = logger;
        _configuration = configuration;
        _configPath = configuration.GetValue<string>("ConfigPath") ?? "tokenrelay.json";
        
        _logger.LogDebug("ConfigurationService initialized with config path: {ConfigPath}", _configPath);
    }

    public TokenRelayConfig GetConfiguration()
    {
        // Check if we should load config from environment variable
        var configMode = Environment.GetEnvironmentVariable("TOKENRELAY_CONFIG_MODE") ?? "file";

        if (configMode.Equals("env", StringComparison.OrdinalIgnoreCase))
        {
            var envConfig = GetConfigurationFromEnvironment();
            if (envConfig != null)
            {
                return envConfig;
            }

            _logger.LogWarning("Environment configuration mode requested but TOKENRELAY_CONFIG_JSON not available, falling back to file configuration");
        }

        // Check if config file exists, if not try environment fallback
        if (!File.Exists(_configPath))
        {
            _logger.LogWarning("Configuration file {ConfigPath} not found, attempting to load from environment", _configPath);
            var envConfig = GetConfigurationFromEnvironment();
            if (envConfig != null)
            {
                return envConfig;
            }

            _logger.LogError("No configuration file found and no environment configuration available. Please mount a config file or provide TOKENRELAY_CONFIG_JSON environment variable.");
            throw new InvalidOperationException($"Configuration file '{_configPath}' not found and no environment configuration available.");
        }

        return _configMonitor.CurrentValue;
    }

    public ProxyConfig GetProxyConfig()
    {
        return GetConfiguration().Proxy;
    }

    public TargetConfig? GetTargetConfig(string targetName)
    {
        _logger.LogDebug("ConfigurationService: Resolving target config for '{TargetName}'", targetName);
        
        // First check runtime overrides
        if (_runtimeTargetOverrides.TryGetValue(targetName, out var overrideTarget))
        {
            _logger.LogDebug("ConfigurationService: Using runtime override for target '{TargetName}' -> {Endpoint}", 
                targetName, overrideTarget.Endpoint);
            
            if (!overrideTarget.Enabled)
            {
                _logger.LogWarning("ConfigurationService: Target '{TargetName}' is disabled in runtime overrides", targetName);
                return null;
            }
            
            return overrideTarget;
        }

        // Fall back to configured targets
        var targets = GetProxyConfig().Targets;
        if (targets.TryGetValue(targetName, out var target))
        {
            _logger.LogDebug("ConfigurationService: Found configured target '{TargetName}' -> {Endpoint}", 
                targetName, target.Endpoint);
            
            if (!target.Enabled)
            {
                _logger.LogWarning("ConfigurationService: Target '{TargetName}' is disabled in configuration", targetName);
                return null;
            }
            
            return target;
        }
        
        _logger.LogWarning("ConfigurationService: Target '{TargetName}' not found in configuration or runtime overrides", targetName);
        return null;
    }

    public bool ValidateAuthToken(string token)
    {
        _logger.LogDebug("ConfigurationService: Validating authentication token");
        
        try
        {
            var authConfig = GetProxyConfig().Auth;
            var encryptionKey = authConfig.EncryptionKey;

            if (!authConfig.Tokens.Any())
            {
                _logger.LogWarning("ConfigurationService: No authentication tokens configured");
                return false;
            }

            _logger.LogDebug("ConfigurationService: Checking token against {TokenCount} configured tokens", 
                authConfig.Tokens.Count);

            // Check against all configured tokens
            foreach (var configToken in authConfig.Tokens)
            {
                if (string.IsNullOrEmpty(configToken)) 
                {
                    _logger.LogDebug("ConfigurationService: Skipping empty token in configuration");
                    continue;
                }

                string tokenToCompare;

                // Check if the token is encrypted (starts with our marker)
                if (configToken.StartsWith("ENC:"))
                {
                    if (string.IsNullOrEmpty(encryptionKey))
                    {
                        _logger.LogWarning("ConfigurationService: Encrypted token found but no encryption key configured");
                        continue;
                    }

                    _logger.LogDebug("ConfigurationService: Decrypting token for comparison");
                    // Decrypt the stored token using AES
                    tokenToCompare = DecryptToken(configToken, encryptionKey);
                }
                else
                {
                    // Plain text comparison for backward compatibility
                    if (string.IsNullOrEmpty(encryptionKey))
                    {
                        _logger.LogWarning("ConfigurationService: Using plain text token comparison - consider encrypting your tokens");
                    }
                    _logger.LogDebug("ConfigurationService: Using plain text token comparison");
                    tokenToCompare = configToken;
                }

                if (string.Equals(token, tokenToCompare, StringComparison.Ordinal))
                {
                    _logger.LogDebug("ConfigurationService: Token validation successful");
                    return true;
                }
            }

            _logger.LogWarning("ConfigurationService: Token validation failed - no matching token found");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConfigurationService: Failed to validate auth token");
            return false;
        }
    }

    public async Task ReloadConfigurationAsync()
    {
        try
        {
            // Force configuration reload
            _logger.LogInformation("Reloading configuration from {ConfigPath}", _configPath);
            
            // The IOptionsMonitor automatically reloads when the file changes
            // This method is for manual reload if needed
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload configuration");
            throw;
        }
    }

    private string DecryptToken(string encryptedToken, string encryptionKey)
    {
        try
        {
            if (string.IsNullOrEmpty(encryptedToken) || string.IsNullOrEmpty(encryptionKey))
            {
                return encryptedToken;
            }

            // Check if the token is encrypted (starts with our marker)
            if (!encryptedToken.StartsWith("ENC:"))
            {
                return encryptedToken; // Return as-is if not encrypted
            }

            // Remove the ENC: prefix
            var actualEncryptedData = encryptedToken.Substring(4);

            // Use AES decryption with the provided key
            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32)); // Ensure 32 bytes
            aes.Key = keyBytes;

            var encryptedBytes = Convert.FromBase64String(actualEncryptedData);

            // First 16 bytes are the IV
            var iv = encryptedBytes.Take(16).ToArray();
            var cipherText = encryptedBytes.Skip(16).ToArray();

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(cipherText);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt token using AES");
            return encryptedToken; // Return as-is if decryption fails
        }
    }

    private TokenRelayConfig? GetConfigurationFromEnvironment()
    {
        try
        {
            // Use Environment.GetEnvironmentVariable for direct access to environment variables
            var configJson = Environment.GetEnvironmentVariable("TOKENRELAY_CONFIG_JSON");
            if (string.IsNullOrEmpty(configJson))
            {
                _logger.LogWarning("TOKENRELAY_CONFIG_JSON environment variable is empty or not set");
                return null;
            }

            _logger.LogInformation("Loading configuration from TOKENRELAY_CONFIG_JSON environment variable");
            var config = System.Text.Json.JsonSerializer.Deserialize<TokenRelayConfig>(configJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config != null)
            {
                _logger.LogInformation("Successfully loaded configuration from environment variable");
                return config;
            }

            _logger.LogWarning("Failed to deserialize configuration from environment variable");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration from environment variable");
            return null;
        }
    }

    public string EncryptToken(string plainToken, string encryptionKey)
    {
        try
        {
            if (string.IsNullOrEmpty(plainToken) || string.IsNullOrEmpty(encryptionKey))
            {
                return plainToken;
            }

            // Use AES encryption with the provided key
            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32)); // Ensure 32 bytes
            aes.Key = keyBytes;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using var swEncrypt = new StreamWriter(csEncrypt);
            
            swEncrypt.Write(plainToken);
            csEncrypt.FlushFinalBlock();
            
            // Combine IV and encrypted data
            var result = aes.IV.Concat(msEncrypt.ToArray()).ToArray();
            
            // Return with ENC: prefix to indicate encryption
            return "ENC:" + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt token using AES");
            return plainToken; // Return as-is if encryption fails
        }
    }

    public void SetRuntimeTargetConfigOverrides(Dictionary<string, TargetConfig> overrides)
    {
        _runtimeTargetOverrides.Clear();
        foreach (var kvp in overrides)
        {
            _runtimeTargetOverrides[kvp.Key] = kvp.Value;
        }
        _logger.LogInformation("Runtime target configuration overrides set for {Count} targets", overrides.Count);
    }

    public Dictionary<string, TargetConfig> GetRuntimeTargetConfigOverrides()
    {
        return new Dictionary<string, TargetConfig>(_runtimeTargetOverrides);
    }

    public void ClearRuntimeTargetConfigOverrides()
    {
        var count = _runtimeTargetOverrides.Count;
        _runtimeTargetOverrides.Clear();
        _logger.LogInformation("Cleared {Count} runtime target configuration overrides", count);
    }
}
