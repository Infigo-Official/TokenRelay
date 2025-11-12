using Microsoft.Extensions.Logging;

namespace TokenRelay.Services;

public interface ILogLevelService
{
    void SetLogLevel(LogLevel logLevel);
    LogLevel GetCurrentLogLevel();
    string GetCurrentLogLevelString();
    LogLevel GetConfiguredLogLevel();
    string GetConfiguredLogLevelString();
    bool IsLogLevelOverridden();
    bool IsValidLogLevel(string logLevel);
}

/// <summary>
/// Static class to hold the runtime log level that can be accessed by the logging filter
/// </summary>
public static class RuntimeLogLevel
{
    private static LogLevel _currentLogLevel = LogLevel.Information;
    
    public static LogLevel Current => _currentLogLevel;
    
    public static void SetLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
    }
    
    public static bool IsEnabled(string? category, LogLevel level)
    {
        // Allow Microsoft.Hosting.Lifetime startup messages at Information level
        if (category == "Microsoft.Hosting.Lifetime")
            return level >= LogLevel.Information;
        
        // Keep other Microsoft logs at Warning level to reduce noise
        if (category != null && category.StartsWith("Microsoft"))
            return level >= LogLevel.Warning;
        
        // For all other categories, use the current runtime log level
        return level >= _currentLogLevel;
    }
}

public class LogLevelService : ILogLevelService
{
    private readonly IMemoryLogService _memoryLogService;
    private readonly IConfiguration _configuration;
    private LogLevel _currentLogLevel = LogLevel.Information;
    private LogLevel _configuredLogLevel = LogLevel.Information;

    public LogLevelService(IMemoryLogService memoryLogService, IConfiguration configuration)
    {
        _memoryLogService = memoryLogService;
        _configuration = configuration;
        
        // Get configured log level from configuration
        var configuredLevelString = _configuration.GetSection("Logging:Level")?.Value ?? "Information";
        if (Enum.TryParse<LogLevel>(configuredLevelString, true, out var configuredLevel))
        {
            _configuredLogLevel = configuredLevel;
            _currentLogLevel = configuredLevel;
        }
    }

    public void SetLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
        _memoryLogService.SetLogLevel(logLevel);
        
        // Update the static runtime log level
        RuntimeLogLevel.SetLogLevel(logLevel);
    }

    public LogLevel GetCurrentLogLevel()
    {
        return _currentLogLevel;
    }

    public string GetCurrentLogLevelString()
    {
        return _currentLogLevel.ToString();
    }

    public LogLevel GetConfiguredLogLevel()
    {
        return _configuredLogLevel;
    }

    public string GetConfiguredLogLevelString()
    {
        return _configuredLogLevel.ToString();
    }

    public bool IsLogLevelOverridden()
    {
        return _currentLogLevel != _configuredLogLevel;
    }

    public bool IsValidLogLevel(string logLevel)
    {
        return Enum.TryParse<LogLevel>(logLevel, true, out _);
    }
}
