using System.Text;

namespace TokenRelay.Middleware;

/// <summary>
/// A DelegatingHandler that provides comprehensive HTTP request and response logging
/// when the application is running in Debug log level.
/// </summary>
public class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<HttpLoggingHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Only log full request/response bodies when in Debug mode
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var requestId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Log request details
            await LogRequestAsync(request, requestId);

            // Send the request
            var response = await base.SendAsync(request, cancellationToken);
            
            stopwatch.Stop();

            // Log response details
            await LogResponseAsync(response, requestId, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "HttpLoggingHandler: Request {RequestId} failed after {ElapsedMs}ms", 
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task LogRequestAsync(HttpRequestMessage request, string requestId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== HTTP REQUEST {requestId} ===");
        sb.AppendLine($"{request.Method} {request.RequestUri}");
        sb.AppendLine($"HTTP/{request.Version}");
        
        // Log headers
        sb.AppendLine("Headers:");
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"  {header.Key}: {value}");
            }
        }

        // Log content headers and body
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.AppendLine($"  {header.Key}: {value}");
                }
            }

            sb.AppendLine();
            
            try
            {
                var contentType = request.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                
                // Only log text-based content to avoid logging binary data
                if (IsTextContent(contentType))
                {
                    var content = await request.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        sb.AppendLine($"Body ({content.Length} chars):");
                        sb.AppendLine(content);
                    }
                    else
                    {
                        sb.AppendLine("Body: (empty)");
                    }
                }
                else
                {
                    var contentLength = request.Content.Headers.ContentLength ?? 0;
                    sb.AppendLine($"Body: (binary content, {contentLength} bytes, content-type: {contentType})");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Body: (error reading content: {ex.Message})");
            }
        }
        else
        {
            sb.AppendLine("Body: (no content)");
        }

        sb.AppendLine("=== END REQUEST ===");
        
        _logger.LogDebug("HttpLoggingHandler: {RequestLog}", sb.ToString());
    }

    private async Task LogResponseAsync(HttpResponseMessage response, string requestId, long elapsedMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== HTTP RESPONSE {requestId} ({elapsedMs}ms) ===");
        sb.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.StatusCode}");
        
        // Log headers
        sb.AppendLine("Headers:");
        foreach (var header in response.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"  {header.Key}: {value}");
            }
        }

        // Log content headers and body
        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.AppendLine($"  {header.Key}: {value}");
                }
            }

            sb.AppendLine();
            
            try
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                
                // Only log text-based content to avoid logging binary data
                if (IsTextContent(contentType))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        sb.AppendLine($"Body ({content.Length} chars):");
                        sb.AppendLine(content);
                    }
                    else
                    {
                        sb.AppendLine("Body: (empty)");
                    }
                }
                else
                {
                    var contentLength = response.Content.Headers.ContentLength ?? 0;
                    sb.AppendLine($"Body: (binary content, {contentLength} bytes, content-type: {contentType})");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Body: (error reading content: {ex.Message})");
            }
        }
        else
        {
            sb.AppendLine("Body: (no content)");
        }

        sb.AppendLine("=== END RESPONSE ===");
        
        _logger.LogDebug("HttpLoggingHandler: {ResponseLog}", sb.ToString());
    }

    private static bool IsTextContent(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        contentType = contentType.ToLowerInvariant();
        
        return contentType.StartsWith("text/") ||
               contentType.StartsWith("application/json") ||
               contentType.StartsWith("application/xml") ||
               contentType.StartsWith("application/x-www-form-urlencoded") ||
               contentType.StartsWith("application/soap+xml") ||
               contentType.StartsWith("application/javascript") ||
               contentType.StartsWith("application/ecmascript") ||
               contentType.Contains("charset=");
    }
}
