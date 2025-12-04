using TokenRelay.Models;

namespace TokenRelay.Services;

public interface IHttpClientService
{
    /// <summary>
    /// Gets an HttpClient configured for the specified target.
    /// Automatically selects the appropriate client based on certificate validation settings.
    /// </summary>
    /// <param name="target">Target configuration containing certificate validation settings</param>
    /// <param name="timeoutSeconds">Optional timeout in seconds. If not specified, uses default timeout.</param>
    /// <returns>A configured HttpClient instance</returns>
    HttpClient GetClientForTarget(TargetConfig target, int? timeoutSeconds = null);
}

public class HttpClientService : IHttpClientService
{
    // Named client constants - centralized to avoid magic strings
    public const string StandardClientName = "TokenRelayClient";
    public const string IgnoreCertsClientName = "TokenRelayClient-IgnoreCerts";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpClientService> _logger;

    public HttpClientService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpClientService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public HttpClient GetClientForTarget(TargetConfig target, int? timeoutSeconds = null)
    {
        // Select the appropriate named client based on certificate validation settings
        var clientName = target.IgnoreCertificateValidation 
            ? IgnoreCertsClientName 
            : StandardClientName;

        if (target.IgnoreCertificateValidation)
        {
            _logger.LogWarning(
                "HttpClientService: Certificate validation is DISABLED for target endpoint: {Endpoint}. " +
                "This should only be used in development/testing environments!", 
                target.Endpoint);
        }

        var httpClient = _httpClientFactory.CreateClient(clientName);
        
        // Set timeout if specified
        if (timeoutSeconds.HasValue)
        {
            httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds.Value);
            _logger.LogDebug(
                "HttpClientService: Created HttpClient with name '{ClientName}', timeout {TimeoutSeconds}s",
                clientName, timeoutSeconds.Value);
        }
        else
        {
            _logger.LogDebug(
                "HttpClientService: Created HttpClient with name '{ClientName}', using default timeout",
                clientName);
        }

        return httpClient;
    }
}
