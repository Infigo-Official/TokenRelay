using TokenRelay.Services;

namespace TokenRelay.Services;

public class LogCleanupService : BackgroundService
{
    private readonly IMemoryLogService _memoryLogService;
    private readonly ILogger<LogCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5); // Clean up every 5 minutes

    public LogCleanupService(IMemoryLogService memoryLogService, ILogger<LogCleanupService> logger)
    {
        _memoryLogService = memoryLogService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Log cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _memoryLogService.ClearOldLogs();
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log cleanup");
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Log cleanup service stopped");
    }
}
