using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TokenRelay.Models;

namespace TokenRelay.Services;

public interface IMemoryLogService
{
    void AddLogEntry(LogEntry logEntry);
    LogsResponse GetLogs();
    void SetLogLevel(LogLevel logLevel);
    LogLevel GetCurrentLogLevel();
    void ClearOldLogs();
    void UpdateConfiguration(int bufferMinutes, LogLevel logLevel);
}

public class MemoryLogService : IMemoryLogService
{
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private LogLevel _currentLogLevel = LogLevel.Information;
    private int _bufferMinutes = 20; // Default value

    public MemoryLogService()
    {
        // Initialize with defaults - no configuration dependency
    }

    public void UpdateConfiguration(int bufferMinutes, LogLevel logLevel)
    {
        _bufferMinutes = bufferMinutes;
        _currentLogLevel = logLevel;
    }

    public void AddLogEntry(LogEntry logEntry)
    {
        _logs.Enqueue(logEntry);
        ClearOldLogs();
    }

    public LogsResponse GetLogs()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-_bufferMinutes);

        var recentLogs = _logs
            .Where(log => log.Timestamp >= cutoffTime)
            .OrderByDescending(log => log.Timestamp)
            .ToList();

        return new LogsResponse
        {
            CurrentLogLevel = _currentLogLevel.ToString(),
            BufferMinutes = _bufferMinutes,
            Logs = recentLogs,
            TotalEntries = recentLogs.Count
        };
    }

    public void SetLogLevel(LogLevel logLevel)
    {
        _currentLogLevel = logLevel;
    }

    public LogLevel GetCurrentLogLevel()
    {
        return _currentLogLevel;
    }

    public void ClearOldLogs()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-_bufferMinutes);

        // Remove old log entries
        while (_logs.TryPeek(out var oldestLog) && oldestLog.Timestamp < cutoffTime)
        {
            _logs.TryDequeue(out _);
        }
    }
}

public class MemoryLoggerProvider : ILoggerProvider
{
    private readonly IMemoryLogService _memoryLogService;

    public MemoryLoggerProvider(IMemoryLogService memoryLogService)
    {
        _memoryLogService = memoryLogService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(categoryName, _memoryLogService);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

public class MemoryLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IMemoryLogService _memoryLogService;

    public MemoryLogger(string categoryName, IMemoryLogService memoryLogService)
    {
        _categoryName = categoryName;
        _memoryLogService = memoryLogService;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _memoryLogService.GetCurrentLogLevel();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = _categoryName,
            Message = message,
            Exception = exception?.ToString()
        };

        _memoryLogService.AddLogEntry(logEntry);
    }
}
