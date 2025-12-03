using Microsoft.AspNetCore.Mvc;
using TokenRelay.Services;

namespace TokenRelay.Controllers;

[ApiController]
public class ProxyController : ControllerBase
{
    private readonly IProxyService _proxyService;
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(IProxyService proxyService, ILogger<ProxyController> logger)
    {
        _proxyService = proxyService;
        _logger = logger;
    }

    [HttpGet("proxy/{**path}")]
    [HttpPost("proxy/{**path}")]
    [HttpPut("proxy/{**path}")]
    [HttpDelete("proxy/{**path}")]
    [HttpPatch("proxy/{**path}")]
    [HttpHead("proxy/{**path}")]
    [HttpOptions("proxy/{**path}")]
    public async Task<IActionResult> ForwardRequest(string? path = "")
    {
        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var method = HttpContext.Request.Method;
        var fullPath = path ?? string.Empty;
        string? targetName = null;
        
        _logger.LogDebug("Received {Method} request to proxy path '{Path}' from {ClientIP}", 
            method, fullPath, clientIP);

        try
        {
            // Get target from header
            if (!Request.Headers.TryGetValue("TOKEN-RELAY-TARGET", out var targetHeader))
            {
                _logger.LogWarning("Request to '{Path}' missing TOKEN-RELAY-TARGET header from {ClientIP}", 
                    fullPath, clientIP);
                return BadRequest("TOKEN-RELAY-TARGET header is required");
            }

            targetName = targetHeader.FirstOrDefault();
            if (string.IsNullOrEmpty(targetName))
            {
                _logger.LogWarning("Request to '{Path}' has empty TOKEN-RELAY-TARGET header from {ClientIP}", 
                    fullPath, clientIP);
                return BadRequest("TOKEN-RELAY-TARGET header cannot be empty");
            }

            _logger.LogInformation("Processing {Method} proxy request to target '{TargetName}' path '{Path}' from {ClientIP}", 
                method, targetName, fullPath, clientIP);
            
            // Log request details at debug level
            _logger.LogDebug("Request details - Content-Length: {ContentLength}, Content-Type: {ContentType}, Query: {QueryString}", 
                HttpContext.Request.ContentLength ?? 0, 
                HttpContext.Request.ContentType ?? "none",
                HttpContext.Request.QueryString.HasValue ? HttpContext.Request.QueryString.Value : "none");

            // Forward the request
            using var response = await _proxyService.ForwardRequestAsync(HttpContext, targetName, fullPath);

            _logger.LogInformation("Proxy request completed - {Method} to '{TargetName}' returned {StatusCode} from {ClientIP}", 
                method, targetName, (int)response.StatusCode, clientIP);
            
            _logger.LogDebug("Response details - Status: {StatusCode}, Content-Length: {ContentLength}, Content-Type: {ContentType}", 
                response.StatusCode, 
                response.Content.Headers.ContentLength?.ToString() ?? "unknown",
                response.Content.Headers.ContentType?.ToString() ?? "unknown");

            // Copy response headers (excluding problematic ones)
            foreach (var header in response.Headers)
            {
                if (ShouldExcludeResponseHeader(header.Key))
                {
                    _logger.LogDebug("Excluding response header '{HeaderName}' from forwarded response", header.Key);
                    continue;
                }
                
                if (!Response.Headers.TryAdd(header.Key, header.Value.ToArray()))
                {
                    _logger.LogDebug("Failed to add response header '{HeaderName}' (already exists or invalid)", header.Key);
                }
            }

            foreach (var header in response.Content.Headers)
            {
                if (ShouldExcludeResponseHeader(header.Key))
                {
                    _logger.LogDebug("Excluding content header '{HeaderName}' from forwarded response", header.Key);
                    continue;
                }
                
                if (!Response.Headers.TryAdd(header.Key, header.Value.ToArray()))
                {
                    _logger.LogDebug("Failed to add content header '{HeaderName}' (already exists or invalid)", header.Key);
                }
            }

            // Add TOKEN-RELAY-PROXIED header
            Response.Headers.TryAdd("TOKEN-RELAY-PROXIED", GetProxyVersion());

            // Set status code
            Response.StatusCode = (int)response.StatusCode;

            _logger.LogDebug("Starting to stream response content...");
            
            try
            {
                // Stream response content
                await response.Content.CopyToAsync(Response.Body);
                _logger.LogDebug("Successfully streamed response content");
            }
            catch (Exception streamEx)
            {
                _logger.LogError(streamEx, "Error streaming response content for {Method} request to '{TargetName}'", 
                    method, targetName);
                throw;
            }

            return new EmptyResult();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid target configuration for '{TargetName}' requested from {ClientIP}", 
                targetName ?? "unknown", clientIP);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during {Method} proxy request to '{TargetName}' from {ClientIP}", 
                method, targetName ?? "unknown", clientIP);
            return StatusCode(502, "Bad Gateway: Unable to connect to target service");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout during {Method} proxy request to '{TargetName}' from {ClientIP}", 
                method, targetName ?? "unknown", clientIP);
            return StatusCode(504, "Gateway Timeout: Request to target service timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during {Method} proxy request to '{TargetName}' from {ClientIP}", 
                method, targetName ?? "unknown", clientIP);
            return StatusCode(500, "Internal server error while processing request");
        }
    }

    private static string GetProxyVersion()
    {
        var version = typeof(ProxyController).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        return $"TokenRelay/{version}";
    }

    private static bool ShouldExcludeResponseHeader(string headerName)
    {
        // Exclude headers that can cause issues when forwarding responses
        var excludedHeaders = new[]
        {
            "Transfer-Encoding",
            "Connection",
            "Server",
            "Date" // Let ASP.NET Core set its own Date header
        };

        return excludedHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }
}
