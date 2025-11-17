using System.Net;
using System.Text;
using TokenRelay.Models;

namespace TokenRelay.Services;

public interface IProxyService
{
    Task<HttpResponseMessage> ForwardRequestAsync(HttpContext context, string targetName, string remainingPath);
}

public class ProxyService : IProxyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigurationService _configService;
    private readonly ILogger<ProxyService> _logger;
    private readonly IOAuthService _oauthService;

    public ProxyService(
        IHttpClientFactory httpClientFactory,
        IConfigurationService configService,
        ILogger<ProxyService> logger,
        IOAuthService oauthService)
    {
        _httpClientFactory = httpClientFactory;
        _configService = configService;
        _logger = logger;
        _oauthService = oauthService;
    }

    private HttpClient CreateHttpClientForTarget(TargetConfig target, ProxyConfig proxy)
    {
        var handler = new HttpClientHandler();
        
        if (target.IgnoreCertificateValidation)
        {
            _logger.LogInformation("ProxyService: Certificate validation is DISABLED for target endpoint: {Endpoint}.", 
                target.Endpoint);
            handler.ServerCertificateCustomValidationCallback = 
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(proxy.TimeoutSeconds)
        };
    }

    public async Task<HttpResponseMessage> ForwardRequestAsync(HttpContext context, string targetName, string remainingPath)
    {
        var proxyConfig = _configService.GetProxyConfig();
        var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        _logger.LogDebug("ProxyService: Starting request forwarding for target '{TargetName}', path '{Path}', mode '{Mode}' from {ClientIP}", 
            targetName, remainingPath, proxyConfig.Mode, clientIP);
        
        // Check if we're in chain mode
        if (proxyConfig.Mode.Equals("chain", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("ProxyService: Using chain mode forwarding to downstream proxy");
            return await ForwardToChainAsync(context, targetName, remainingPath);
        }
        
        _logger.LogDebug("ProxyService: Using direct mode forwarding");
        
        // Direct mode (existing behavior)
        var target = _configService.GetTargetConfig(targetName);
        if (target == null)
        {
            _logger.LogWarning("ProxyService: Target '{TargetName}' not found in configuration or not enabled.", targetName);
            throw new ArgumentException($"Target '{targetName}' not found in configuration or not enabled.");
        }

        _logger.LogDebug("ProxyService: Target '{TargetName}' resolved to endpoint '{Endpoint}'", 
            targetName, target.Endpoint);

        var httpClient = CreateHttpClientForTarget(target, proxyConfig);

        _logger.LogDebug("ProxyService: HTTP client configured with timeout {TimeoutSeconds}s", proxyConfig.TimeoutSeconds);

        // Build target URL
        var targetUrl = CombineUrls(target.Endpoint, remainingPath);
        if (!string.IsNullOrEmpty(context.Request.QueryString.Value))
        {
            targetUrl += context.Request.QueryString.Value;
        }

        _logger.LogDebug("ProxyService: Final target URL constructed: {TargetUrl}", SanitizeUrlForLogging(targetUrl));

        // Create the forwarded request
        var forwardedRequest = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            targetUrl);

        _logger.LogDebug("ProxyService: Created {Method} request to {TargetUrl}",
            context.Request.Method, SanitizeUrlForLogging(targetUrl));

        // Copy headers (excluding our custom headers)
        var headerCount = 0;
        foreach (var header in context.Request.Headers)
        {
            if (IsHeaderToExclude(header.Key))
            {
                _logger.LogDebug("ProxyService: Excluding header '{HeaderName}' from forwarded request",
                    SanitizeHeaderNameForLogging(header.Key));
                continue;
            }

            // Some headers need to be added to content, not request
            if (IsContentHeader(header.Key))
                continue;

            var headerValue = string.Join(", ", header.Value.ToArray());
            _logger.LogDebug("ProxyService: Adding header '{HeaderName}' with value '{HeaderValue}' to forwarded request",
                SanitizeHeaderNameForLogging(header.Key), SanitizeHeaderValueForLogging(header.Key, headerValue));
            forwardedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            headerCount++;
        }

        _logger.LogDebug("ProxyService: Copied {HeaderCount} headers to forwarded request", headerCount);

        // Add target-specific headers
        if (target.Headers.Any())
        {
            _logger.LogDebug("ProxyService: Adding {CustomHeaderCount} target-specific headers", target.Headers.Count);
            foreach (var header in target.Headers)
            {
                forwardedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                _logger.LogDebug("ProxyService: Added custom header '{HeaderName}': '{HeaderValue}'",
                    header.Key, SanitizeHeaderValueForLogging(header.Key, header.Value));
            }
        }

        // Add OAuth Authorization header if target uses OAuth
        if (target.AuthType.Equals("oauth", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _logger.LogDebug("ProxyService: Target '{TargetName}' uses OAuth, acquiring token", targetName);
                var token = await _oauthService.AcquireTokenAsync(targetName, target, context.RequestAborted);
                var authHeaderValue = token.GetAuthorizationHeaderValue();

                forwardedRequest.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
                _logger.LogDebug("ProxyService: Added OAuth Authorization header for target '{TargetName}' (type: {TokenType})",
                    targetName, token.TokenType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProxyService: Failed to acquire OAuth token for target '{TargetName}'", targetName);
                throw new HttpRequestException(
                    $"Failed to acquire OAuth token for target '{targetName}': {ex.Message}",
                    ex);
            }
        }

        // Add TOKEN-RELAY-ORIGIN header with client IP
        forwardedRequest.Headers.TryAddWithoutValidation("TOKEN-RELAY-ORIGIN", clientIP);
        _logger.LogDebug("ProxyService: Added TOKEN-RELAY-ORIGIN header with client IP: {ClientIP}", clientIP);

        // Copy request body if present
        if (context.Request.ContentLength > 0 ||
            context.Request.Headers.TransferEncoding.Any(te => te == "chunked"))
        {
            _logger.LogDebug("ProxyService: Request has body content - Length: {ContentLength}, Transfer-Encoding: {TransferEncoding}", 
                context.Request.ContentLength?.ToString() ?? "chunked", 
                string.Join(", ", context.Request.Headers.TransferEncoding.Select(te => te ?? "unknown")));

            var content = new StreamContent(context.Request.Body);

            // Copy content headers
            var contentHeaderCount = 0;
            foreach (var header in context.Request.Headers)
            {
                if (IsContentHeader(header.Key))
                {
                    var headerValue = string.Join(", ", header.Value.ToArray());
                    _logger.LogDebug("ProxyService: Copying content header '{HeaderName}' with value '{HeaderValue}'",
                        SanitizeHeaderNameForLogging(header.Key), SanitizeHeaderValueForLogging(header.Key, headerValue));
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    contentHeaderCount++;
                }
            }

            _logger.LogDebug("ProxyService: Copied {ContentHeaderCount} content headers", contentHeaderCount);
            forwardedRequest.Content = content;
        }
        else
        {
            _logger.LogDebug("ProxyService: Request has no body content");
        }

        try
        {
            _logger.LogInformation("ProxyService: Forwarding {Method} request to {TargetUrl} for target '{TargetName}' from {ClientIP}",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName), clientIP);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.SendAsync(forwardedRequest, HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();

            _logger.LogInformation("ProxyService: Received {StatusCode} response from {TargetUrl} in {ElapsedMs}ms",
                response.StatusCode, SanitizeUrlForLogging(targetUrl), stopwatch.ElapsedMilliseconds);

            _logger.LogDebug("ProxyService: Response details - Status: {StatusCode}, Content-Length: {ContentLength}, Content-Type: {ContentType}, Headers: {HeaderCount}", 
                response.StatusCode,
                response.Content.Headers.ContentLength?.ToString() ?? "unknown",
                response.Content.Headers.ContentType?.ToString() ?? "unknown",
                response.Headers.Count() + response.Content.Headers.Count());

            _logger.LogDebug("ProxyService: Response: {StatusCode} - Headers: {Headers}", 
                response.StatusCode, 
                string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")));
            

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ProxyService: HTTP error forwarding {Method} request to {TargetUrl} for target '{TargetName}'",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName));
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "ProxyService: Timeout forwarding {Method} request to {TargetUrl} for target '{TargetName}' (timeout: {TimeoutSeconds}s)",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName), proxyConfig.TimeoutSeconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "ProxyService: Request cancelled while forwarding {Method} request to {TargetUrl} for target '{TargetName}'",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProxyService: Unexpected error forwarding {Method} request to {TargetUrl} for target '{TargetName}'",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName));
            throw;
        }
    }

    private async Task<HttpResponseMessage> ForwardToChainAsync(HttpContext context, string targetName, string remainingPath)
    {
        var proxyConfig = _configService.GetProxyConfig();
        var chainTarget = proxyConfig.Chain.TargetProxy;
        var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        _logger.LogDebug("ProxyService: Chain mode - forwarding to downstream proxy '{ChainEndpoint}' for target '{TargetName}' from {ClientIP}", 
            chainTarget.Endpoint, targetName, clientIP);
        
        if (string.IsNullOrEmpty(chainTarget.Endpoint))
        {
            _logger.LogError("ProxyService: Chain mode enabled but no target proxy endpoint configured");
            throw new InvalidOperationException("Chain mode is enabled but no target proxy endpoint configured");
        }

        var httpClient = CreateHttpClientForTarget(chainTarget, proxyConfig);

        // Build target URL - forward to the chain proxy with the same path
        var targetUrl = CombineUrls(chainTarget.Endpoint, $"proxy/{remainingPath}");
        if (!string.IsNullOrEmpty(context.Request.QueryString.Value))
        {
            targetUrl += context.Request.QueryString.Value;
        }

        _logger.LogDebug("ProxyService: Chain target URL constructed: {TargetUrl}", SanitizeUrlForLogging(targetUrl));

        // Create the forwarded request
        var forwardedRequest = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            targetUrl);

        // Copy headers (excluding our custom headers)
        var headerCount = 0;
        foreach (var header in context.Request.Headers)
        {
            if (IsHeaderToExclude(header.Key))
            {
                _logger.LogDebug("ProxyService: Chain mode - excluding header '{HeaderName}'",
                    SanitizeHeaderNameForLogging(header.Key));
                continue;
            }

            // Some headers need to be added to content, not request
            if (IsContentHeader(header.Key))
                continue;

            forwardedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            headerCount++;
        }

        _logger.LogDebug("ProxyService: Chain mode - copied {HeaderCount} headers", headerCount);

        // Add chain target-specific headers
        if (chainTarget.Headers.Any())
        {
            _logger.LogDebug("ProxyService: Chain mode - adding {ChainHeaderCount} chain-specific headers", chainTarget.Headers.Count);
            foreach (var header in chainTarget.Headers)
            {
                forwardedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Add OAuth Authorization header for chain target if configured
        if (chainTarget.AuthType.Equals("oauth", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _logger.LogDebug("ProxyService: Chain target uses OAuth, acquiring token");
                var token = await _oauthService.AcquireTokenAsync("__chain_target__", chainTarget, context.RequestAborted);
                var authHeaderValue = token.GetAuthorizationHeaderValue();

                forwardedRequest.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
                _logger.LogDebug("ProxyService: Added OAuth Authorization header for chain target (type: {TokenType})",
                    token.TokenType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProxyService: Failed to acquire OAuth token for chain target");
                throw new HttpRequestException(
                    $"Failed to acquire OAuth token for chain target: {ex.Message}",
                    ex);
            }
        }

        // Preserve the original TOKEN-RELAY-TARGET header so the downstream proxy knows the target
        forwardedRequest.Headers.TryAddWithoutValidation("TOKEN-RELAY-TARGET", targetName);
        _logger.LogDebug("ProxyService: Chain mode - preserved TOKEN-RELAY-TARGET: {TargetName}", targetName);

        // Set the auth token for the target proxy
        forwardedRequest.Headers.TryAddWithoutValidation("TOKEN-RELAY-AUTH", $"{proxyConfig.Chain.TargetProxy.Token}");
        _logger.LogDebug("ProxyService: Chain mode - added authentication token for downstream proxy");

        // Add TOKEN-RELAY-ORIGIN header with client IP
        forwardedRequest.Headers.TryAddWithoutValidation("TOKEN-RELAY-ORIGIN", clientIP);

        // Add TOKEN-RELAY-CHAIN header to indicate this is a chained request
        forwardedRequest.Headers.TryAddWithoutValidation("TOKEN-RELAY-CHAIN", "true");
        _logger.LogDebug("ProxyService: Chain mode - marked request as chained");

        // Copy request body if present
        if (context.Request.ContentLength > 0 ||
            context.Request.Headers.TransferEncoding.Any(te => te == "chunked"))
        {
            _logger.LogDebug("ProxyService: Chain mode - buffering request body for reliable transmission");
            
            // For chain mode, buffer the request body to ensure reliable transmission
            // between proxy instances using CopyToAsync for all cases
            _logger.LogDebug("ProxyService: Chain mode - reading request body (Content-Length: {ContentLength})", 
                context.Request.ContentLength?.ToString() ?? "unknown");
            
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream);
            var content = new ByteArrayContent(memoryStream.ToArray());
            _logger.LogDebug("ProxyService: Chain mode - buffered {BufferedSize} bytes from stream (original Content-Length: {OriginalLength})", 
                memoryStream.Length, context.Request.ContentLength?.ToString() ?? "unknown");

            // Copy content headers (but let ByteArrayContent set Content-Length)
            var contentHeaderCount = 0;
            foreach (var header in context.Request.Headers)
            {
                if (IsContentHeader(header.Key))
                {
                    // Skip Content-Length as ByteArrayContent will set it correctly
                    if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("ProxyService: Chain mode - skipping Content-Length header (will be set by ByteArrayContent)");
                        continue;
                    }
                    
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    contentHeaderCount++;
                    var headerValue = string.Join(", ", header.Value.ToArray());
                    _logger.LogDebug("ProxyService: Chain mode - copied content header '{HeaderName}' with value '{HeaderValue}'",
                        SanitizeHeaderNameForLogging(header.Key), SanitizeHeaderValueForLogging(header.Key, headerValue));
                }
            }
            _logger.LogDebug("ProxyService: Chain mode - copied {ContentHeaderCount} content headers", contentHeaderCount);

            forwardedRequest.Content = content;
        }
        else
        {
            _logger.LogDebug("ProxyService: Chain mode - no request body to buffer");
        }

        try
        {
            _logger.LogInformation("ProxyService: Chain mode - forwarding {Method} request to downstream proxy {TargetUrl} for target '{TargetName}' from {ClientIP}",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), SanitizeForLogging(targetName), clientIP);

            _logger.LogDebug("ProxyService: Chain mode - request details before sending: Content-Length: {ContentLength}, Headers: {HeaderCount}",
                forwardedRequest.Content?.Headers.ContentLength?.ToString() ?? "none",
                forwardedRequest.Headers.Count());

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.SendAsync(forwardedRequest, HttpCompletionOption.ResponseHeadersRead);
            stopwatch.Stop();

            _logger.LogInformation("ProxyService: Chain mode - received {StatusCode} response from downstream proxy {TargetUrl} in {ElapsedMs}ms",
                response.StatusCode, SanitizeUrlForLogging(targetUrl), stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "ProxyService: Chain mode - timeout forwarding {Method} request to downstream proxy {TargetUrl} (timeout: {TimeoutSeconds}s)",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl), proxyConfig.TimeoutSeconds);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ProxyService: Chain mode - HTTP error forwarding {Method} request to downstream proxy {TargetUrl}",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProxyService: Chain mode - unexpected error forwarding {Method} request to downstream proxy {TargetUrl}",
                SanitizeForLogging(context.Request.Method), SanitizeUrlForLogging(targetUrl));
            throw;
        }
    }

    private static string CombineUrls(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.TrimStart('/');
        
        if (string.IsNullOrEmpty(trimmedPath))
            return trimmedBase;
            
        return $"{trimmedBase}/{trimmedPath}";
    }

    private static bool IsHeaderToExclude(string headerName)
    {
        var excludedHeaders = new[]
        {
            "TOKEN-RELAY-AUTH",
            "TOKEN-RELAY-TARGET",
            "Host",
            "Connection",
            "Transfer-Encoding"
        };

        return excludedHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsContentHeader(string headerName)
    {
        var contentHeaders = new[]
        {
            "Content-Type",
            "Content-Length",
            "Content-Encoding",
            "Content-Language",
            "Content-Location",
            "Content-MD5",
            "Content-Range",
            "Expires",
            "Last-Modified"
        };

        return contentHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetClientIPAddress(HttpContext context)
    {
        // Check for forwarded headers first (proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in case of multiple proxies
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIP = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIP))
        {
            return realIP;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Sanitizes a URL for logging by removing query string parameters that might contain sensitive data.
    /// </summary>
    private static string SanitizeUrlForLogging(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        try
        {
            var uri = new Uri(url);
            // Return URL without query string to avoid logging sensitive parameters
            return uri.GetLeftPart(UriPartial.Path);
        }
        catch
        {
            // If URL parsing fails, return a generic placeholder
            return "[invalid-url]";
        }
    }

    /// <summary>
    /// Sanitizes header value for logging by masking sensitive information.
    /// </summary>
    private static string SanitizeHeaderValueForLogging(string headerName, string headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
            return headerValue;

        // List of sensitive headers that should be masked
        var sensitiveHeaders = new[]
        {
            "Authorization",
            "TOKEN-RELAY-AUTH",
            "X-API-Key",
            "API-Key",
            "Cookie",
            "Set-Cookie"
        };

        if (sensitiveHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase))
        {
            return "[REDACTED]";
        }

        // Truncate long values to prevent log bloat
        return headerValue.Length > 100 ? $"{headerValue[..100]}..." : headerValue;
    }

    /// <summary>
    /// Sanitizes header name for logging to prevent log injection attacks.
    /// </summary>
    private static string SanitizeHeaderNameForLogging(string headerName)
    {
        if (string.IsNullOrEmpty(headerName))
            return headerName ?? string.Empty;

        // Remove newline characters and other control characters
        return headerName
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\t", " ");
    }

    /// <summary>
    /// Sanitizes user-provided strings for logging to prevent log injection attacks.
    /// </summary>
    private static string SanitizeForLogging(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        // Remove newline characters and other control characters that could be used for log injection
        return input
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\t", " ");
    }
}
