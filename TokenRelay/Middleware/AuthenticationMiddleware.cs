using TokenRelay.Services;

namespace TokenRelay.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfigurationService configService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var method = context.Request.Method;
        
        _logger.LogDebug("AuthenticationMiddleware: Received {Method} request for path '{Path}' from {ClientIP}",
            method, path, clientIP);

        // Skip authentication for specific excluded paths
        if (IsExcludedPath(path))
        {
            _logger.LogDebug("AuthenticationMiddleware: Skipping authentication for excluded path '{Path}' from {ClientIP}",
                path, clientIP);
            await _next(context);
            return;
        }

        _logger.LogDebug("AuthenticationMiddleware: Processing authentication for path '{Path}' from {ClientIP}",
            path, clientIP);

        // Check for authentication header
        if (!context.Request.Headers.TryGetValue("TOKEN-RELAY-AUTH", out var authHeader))
        {
            _logger.LogWarning("AuthenticationMiddleware: Request to '{Path}' missing TOKEN-RELAY-AUTH header from {ClientIP}",
                path, clientIP);

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: TOKEN-RELAY-AUTH header required");
            return;
        }

        var token = authHeader.FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("AuthenticationMiddleware: Request to '{Path}' has empty TOKEN-RELAY-AUTH header from {ClientIP}", 
                path, clientIP);
            
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: TOKEN-RELAY-AUTH header cannot be empty");
            return;
        }

        _logger.LogDebug("AuthenticationMiddleware: Validating TOKEN-RELAY-AUTH token for path '{Path}' from {ClientIP}", 
            path, clientIP);

        // Validate token
        if (!configService.ValidateAuthToken(token))
        {
            _logger.LogWarning("AuthenticationMiddleware: Request to '{Path}' has invalid TOKEN-RELAY-AUTH token from {ClientIP}", 
                path, clientIP);
            
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: Invalid TOKEN-RELAY-AUTH token");
            return;
        }

        _logger.LogDebug("AuthenticationMiddleware: Authentication successful for path '{Path}' from {ClientIP}", 
            path, clientIP);

        // Token is valid, continue to next middleware
        await _next(context);
    }

    private static bool IsExcludedPath(string path)
    {
        // Root path (Swagger UI)
        if (path == "/" || path == string.Empty)
            return true;

        // Health check endpoints
        if (path == "/health" || path.StartsWith("/health/"))
            return true;

        // Swagger endpoints
        if (path.StartsWith("/swagger"))
            return true;

        return false;
    }
}
