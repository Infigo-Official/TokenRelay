using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Collections.Concurrent;

namespace TokenRelay.Services;

public interface IRuntimeLoggingService
{
    void UpdateLogLevel(LogLevel logLevel);
    LogLevel GetCurrentLogLevel();
}

/// <summary>
/// A service that manages runtime logging configuration changes
/// </summary>
public class RuntimeLoggingService : IRuntimeLoggingService
{
    private readonly ConcurrentDictionary<string, RuntimeLoggerProvider> _providers = new();
    private LogLevel _currentLogLevel = LogLevel.Information;

    public void UpdateLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
        
        // Update all registered providers
        foreach (var provider in _providers.Values)
        {
            provider.UpdateLogLevel(logLevel);
        }
    }

    public LogLevel GetCurrentLogLevel()
    {
        return _currentLogLevel;
    }

    public void RegisterProvider(string name, RuntimeLoggerProvider provider)
    {
        _providers[name] = provider;
    }

    public void UnregisterProvider(string name)
    {
        _providers.TryRemove(name, out _);
    }
}

/// <summary>
/// A logger provider that can be reconfigured at runtime
/// </summary>
public class RuntimeLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, RuntimeLogger> _loggers = new();
    private readonly IRuntimeLoggingService _runtimeLoggingService;
    private LogLevel _currentLogLevel = LogLevel.Information;

    public RuntimeLoggerProvider(IRuntimeLoggingService runtimeLoggingService)
    {
        _runtimeLoggingService = runtimeLoggingService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new RuntimeLogger(name, this));
    }

    public void UpdateLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
        
        // Update all existing loggers
        foreach (var logger in _loggers.Values)
        {
            logger.UpdateLogLevel(logLevel);
        }
    }

    public LogLevel GetCurrentLogLevel()
    {
        return _currentLogLevel;
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

/// <summary>
/// A logger that can be reconfigured at runtime
/// </summary>
public class RuntimeLogger : ILogger
{
    private readonly string _categoryName;
    private readonly RuntimeLoggerProvider _provider;
    private LogLevel _currentLogLevel = LogLevel.Information;

    public RuntimeLogger(string categoryName, RuntimeLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Apply the same filtering logic as the main application
        if (_categoryName == "Microsoft.Hosting.Lifetime")
            return logLevel >= LogLevel.Information;
        
        if (_categoryName != null && _categoryName.StartsWith("Microsoft"))
            return logLevel >= LogLevel.Warning;
        
        return logLevel >= _provider.GetCurrentLogLevel();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]");
        var logLevelString = GetLogLevelString(logLevel);
        
        // Format the message similar to SimpleConsole formatter
        var formattedMessage = $"{timestamp} {logLevelString}: {_categoryName}[{eventId.Id}] {message}";
        
        if (exception != null)
        {
            formattedMessage += Environment.NewLine + exception.ToString();
        }

        Console.WriteLine(formattedMessage);
    }

    public void UpdateLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            LogLevel.None => "none",
            _ => "unkn"
        };
    }
}
