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
        
        // Log headers (sanitizing sensitive ones)
        sb.AppendLine("Headers:");
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"  {header.Key}: {SanitizeHeaderValue(header.Key, value)}");
            }
        }

        // Log content headers and body
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.AppendLine($"  {header.Key}: {SanitizeHeaderValue(header.Key, value)}");
                }
            }

            sb.AppendLine();

            try
            {
                var contentType = request.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                // Only log text-based content to avoid logging binary data
                // But sanitize potential sensitive content (like OAuth credentials)
                if (IsTextContent(contentType))
                {
                    var content = await request.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        var sanitizedContent = SanitizeBodyContent(content, contentType);
                        sb.AppendLine($"Body ({content.Length} chars):");
                        sb.AppendLine(sanitizedContent);
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
        
        // Log headers (sanitizing sensitive ones)
        sb.AppendLine("Headers:");
        foreach (var header in response.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"  {header.Key}: {SanitizeHeaderValue(header.Key, value)}");
            }
        }

        // Log content headers and body
        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.AppendLine($"  {header.Key}: {SanitizeHeaderValue(header.Key, value)}");
                }
            }

            sb.AppendLine();

            try
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                // Only log text-based content to avoid logging binary data
                // But sanitize potential sensitive content (like OAuth token responses)
                if (IsTextContent(contentType))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        var sanitizedContent = SanitizeBodyContent(content, contentType);
                        sb.AppendLine($"Body ({content.Length} chars):");
                        sb.AppendLine(sanitizedContent);
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

    /// <summary>
    /// Sanitizes header values to prevent logging sensitive information.
    /// </summary>
    private static string SanitizeHeaderValue(string headerName, string value)
    {
        // List of sensitive headers that should be redacted
        var sensitiveHeaders = new[]
        {
            "Authorization",
            "TOKEN-RELAY-AUTH",
            "X-API-Key",
            "API-Key",
            "Cookie",
            "Set-Cookie",
            "X-Auth-Token",
            "X-Access-Token",
            "X-Refresh-Token",
            "WWW-Authenticate",
            "Proxy-Authorization",
            "Proxy-Authenticate"
        };

        if (sensitiveHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase))
        {
            return "[REDACTED]";
        }

        return value;
    }

    /// <summary>
    /// Sanitizes body content to prevent logging sensitive information like tokens and credentials.
    /// </summary>
    private static string SanitizeBodyContent(string content, string contentType)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // For JSON content, try to redact sensitive fields
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(content);
                if (json != null)
                {
                    var sensitiveFields = new[] { "access_token", "refresh_token", "id_token", "token", "secret", "password", "client_secret", "api_key", "apikey" };
                    var sanitized = new Dictionary<string, object>();

                    foreach (var kvp in json)
                    {
                        if (sensitiveFields.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            sanitized[kvp.Key] = "[REDACTED]";
                        }
                        else
                        {
                            sanitized[kvp.Key] = kvp.Value.ToString();
                        }
                    }

                    return System.Text.Json.JsonSerializer.Serialize(sanitized);
                }
            }
            catch
            {
                // If JSON parsing fails, return the original content
            }
        }

        // For form-urlencoded content, redact sensitive parameters
        if (contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var sensitiveParams = new[] { "client_secret", "password", "token", "access_token", "refresh_token", "secret", "api_key", "apikey" };
            var parts = content.Split('&');
            var sanitizedParts = new List<string>();

            foreach (var part in parts)
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2 && sensitiveParams.Contains(keyValue[0], StringComparer.OrdinalIgnoreCase))
                {
                    sanitizedParts.Add($"{keyValue[0]}=[REDACTED]");
                }
                else
                {
                    sanitizedParts.Add(part);
                }
            }

            return string.Join("&", sanitizedParts);
        }

        return content;
    }
}
