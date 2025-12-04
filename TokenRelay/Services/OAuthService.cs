using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TokenRelay.Models;

namespace TokenRelay.Services;

public interface IOAuthService
{
    /// <summary>
    /// Acquires an OAuth token for a target, using cache if available and valid.
    /// Thread-safe operation.
    /// </summary>
    Task<OAuthToken> AcquireTokenAsync(string targetName, TargetConfig target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces token refresh for a specific target, bypassing cache.
    /// </summary>
    Task<OAuthToken> RefreshTokenAsync(string targetName, TargetConfig target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears cached token for a specific target.
    /// </summary>
    void ClearTokenCache(string targetName);

    /// <summary>
    /// Clears all cached tokens.
    /// </summary>
    void ClearAllTokens();

    /// <summary>
    /// Gets cache statistics for monitoring/debugging.
    /// </summary>
    Dictionary<string, object> GetCacheStatistics();
}

public class OAuthService : IOAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthService> _logger;

    // Thread-safe token cache: targetName -> OAuthToken
    private readonly ConcurrentDictionary<string, OAuthToken> _tokenCache = new();

    // Semaphores for token acquisition to prevent concurrent requests for same target
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetLocks = new();

    // Statistics tracking
    private long _cacheHits = 0;
    private long _cacheMisses = 0;
    private long _tokenAcquisitions = 0;
    private long _tokenRefreshes = 0;
    private long _tokenAcquisitionFailures = 0;

    public OAuthService(
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OAuthToken> AcquireTokenAsync(
        string targetName,
        TargetConfig target,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OAuthService: Token acquisition requested for target '{TargetName}'", targetName);

        // Validation
        ValidateTargetConfig(targetName, target);

        // Check cache first
        if (_tokenCache.TryGetValue(targetName, out var cachedToken))
        {
            if (!cachedToken.IsExpired())
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("OAuthService: Using cached token for target '{TargetName}' (expires at {ExpiresAt})",
                    targetName, cachedToken.ExpiresAt);
                return cachedToken;
            }

            _logger.LogDebug("OAuthService: Cached token for target '{TargetName}' is expired, acquiring new token", targetName);
        }

        Interlocked.Increment(ref _cacheMisses);

        // Get or create semaphore for this target
        var targetLock = _targetLocks.GetOrAdd(targetName, _ => new SemaphoreSlim(1, 1));

        // Wait for lock (thread-safe token acquisition)
        await targetLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock (another thread might have just acquired token)
            if (_tokenCache.TryGetValue(targetName, out var recentToken) && !recentToken.IsExpired())
            {
                _logger.LogDebug("OAuthService: Token was acquired by another thread while waiting, using it");
                return recentToken;
            }

            // Acquire new token
            return await AcquireNewTokenAsync(targetName, target, cancellationToken);
        }
        finally
        {
            targetLock.Release();
        }
    }

    public async Task<OAuthToken> RefreshTokenAsync(
        string targetName,
        TargetConfig target,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("OAuthService: Forced token refresh requested for target '{TargetName}'", targetName);

        ValidateTargetConfig(targetName, target);

        var targetLock = _targetLocks.GetOrAdd(targetName, _ => new SemaphoreSlim(1, 1));
        await targetLock.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _tokenRefreshes);
            return await AcquireNewTokenAsync(targetName, target, cancellationToken);
        }
        finally
        {
            targetLock.Release();
        }
    }

    private async Task<OAuthToken> AcquireNewTokenAsync(
        string targetName,
        TargetConfig target,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("OAuthService: Acquiring new OAuth token for target '{TargetName}'", targetName);
        Interlocked.Increment(ref _tokenAcquisitions);

        var authData = target.AuthData;
        var tokenEndpoint = GetOrBuildTokenEndpoint(target);

        try
        {
            // Build form data based on grant type
            var formData = BuildTokenRequestFormData(authData);

            // Log request details (sanitized)
            _logger.LogDebug("OAuthService: Requesting token from endpoint '{TokenEndpoint}' for target '{TargetName}'",
                tokenEndpoint, targetName);
            _logger.LogDebug("OAuthService: Grant type: '{GrantType}', Client ID: '{ClientId}', Scope: '{Scope}'",
                authData.GetValueOrDefault("grant_type"),
                authData.GetValueOrDefault("client_id"),
                authData.GetValueOrDefault("scope", "none"));

            var httpClient = CreateHttpClient(target);

            // Create a timeout cancellation token source (30 seconds for OAuth requests)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Add Basic Auth if client_secret is provided (but not for password grant type - it goes in form body)
            var grantType = authData.GetValueOrDefault("grant_type")?.ToLowerInvariant();
            if (authData.TryGetValue("client_secret", out var clientSecret) &&
                !string.IsNullOrEmpty(clientSecret) &&
                authData.TryGetValue("client_id", out var clientId) &&
                grantType != "password")
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);

                _logger.LogDebug("OAuthService: Using Basic Authentication with client credentials");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(formData),
                linkedCts.Token);
            stopwatch.Stop();

            var responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // Sanitize response to avoid logging sensitive data (error responses may still contain tokens)
                var sanitizedResponse = SanitizeOAuthResponseForLogging(responseContent);
                _logger.LogError("OAuthService: Token acquisition failed for target '{TargetName}' - Status: {StatusCode}, Response: {Response}",
                    targetName, response.StatusCode, sanitizedResponse);
                Interlocked.Increment(ref _tokenAcquisitionFailures);
                throw new HttpRequestException(
                    $"OAuth token acquisition failed with status {response.StatusCode}");
            }

            _logger.LogInformation("OAuthService: Token acquired successfully for target '{TargetName}' in {ElapsedMs}ms",
                targetName, stopwatch.ElapsedMilliseconds);

            // Parse response
            var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (tokenResponse == null || !tokenResponse.ContainsKey("access_token"))
            {
                _logger.LogError("OAuthService: Invalid token response for target '{TargetName}' - missing 'access_token' field",
                    targetName);
                Interlocked.Increment(ref _tokenAcquisitionFailures);
                throw new InvalidOperationException(
                    "OAuth token response missing 'access_token' field");
            }

            var token = new OAuthToken
            {
                AccessToken = tokenResponse["access_token"].GetString() ?? string.Empty,
                TokenType = tokenResponse.TryGetValue("token_type", out var tt)
                    ? tt.GetString() ?? "Bearer"
                    : "Bearer",
                ExpiresIn = tokenResponse.TryGetValue("expires_in", out var exp)
                    ? exp.GetInt32()
                    : 3600,
                RefreshToken = tokenResponse.TryGetValue("refresh_token", out var rt)
                    ? rt.GetString()
                    : null,
                Scope = tokenResponse.TryGetValue("scope", out var sc)
                    ? sc.GetString()
                    : null,
                AcquiredAt = DateTime.UtcNow
            };

            // Validate that we have a valid access token
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                _logger.LogError("OAuthService: Invalid token response for target '{TargetName}' - access_token is null or empty",
                    targetName);
                Interlocked.Increment(ref _tokenAcquisitionFailures);
                throw new InvalidOperationException(
                    "Invalid token response from endpoint: access_token is null or empty");
            }

            _logger.LogDebug("OAuthService: Token details - Type: '{TokenType}', Expires in: {ExpiresIn}s, Expires at: {ExpiresAt}",
                token.TokenType, token.ExpiresIn, token.ExpiresAt);

            // Cache the token
            _tokenCache[targetName] = token;
            _logger.LogDebug("OAuthService: Token cached for target '{TargetName}'", targetName);

            return token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OAuthService: Unexpected error acquiring token for target '{TargetName}'", targetName);
            Interlocked.Increment(ref _tokenAcquisitionFailures);
            throw;
        }
    }
    
    private HttpClient CreateHttpClient(TargetConfig target)
    {
        if (target.IgnoreCertificateValidation)
        {
            _logger.LogWarning("OAuthService: SSL certificate validation is DISABLED for this target. " +
                               "This should only be used in development/testing environments!");

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    // Accept all certificates
                    _logger.LogDebug("OAuthService: Bypassing SSL certificate validation. Errors: {SslErrors}", errors);
                    return true;
                }
            };

            return new HttpClient(handler);
        }

        // Use factory for standard clients
        return _httpClientFactory.CreateClient();
    }

    private string GetOrBuildTokenEndpoint(TargetConfig target)
    {
        // If token_endpoint is explicitly provided, use it
        if (target.AuthData.TryGetValue("token_endpoint", out var explicitEndpoint) &&
            !string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            _logger.LogDebug("OAuthService: Using explicit token_endpoint: {TokenEndpoint}", explicitEndpoint);
            return explicitEndpoint;
        }

        // Build token endpoint from target endpoint
        var baseEndpoint = target.Endpoint.TrimEnd('/');
        var tokenEndpoint = $"{baseEndpoint}/oauth/token";

        _logger.LogInformation("OAuthService: token_endpoint not provided, built dynamically: {TokenEndpoint}", tokenEndpoint);
        return tokenEndpoint;
    }

    /// <summary>
    /// Validates target configuration for OAuth authentication.
    /// Performs basic validation and delegates to grant-type-specific validation.
    /// </summary>
    private void ValidateTargetConfig(string targetName, TargetConfig target)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target),
                $"Target configuration for '{targetName}' cannot be null");
        }

        if (!target.AuthType.Equals("oauth", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Target '{targetName}' does not have authType='oauth' (actual: '{target.AuthType}')");
        }

        if (target.AuthData == null || target.AuthData.Count == 0)
        {
            throw new ArgumentException(
                $"Target '{targetName}' with authType='oauth' must have authData configured");
        }

        // Delegate to OAuth-specific validation
        ValidateOAuthConfiguration(targetName, target.AuthData);
    }

    /// <summary>
    /// Validates OAuth-specific configuration including grant-type-specific requirements.
    /// Consolidated validation logic for all OAuth grant types.
    /// </summary>
    private void ValidateOAuthConfiguration(string targetName, Dictionary<string, string> authData)
    {
        // Validate grant_type is present (required for all OAuth flows)
        if (!authData.ContainsKey("grant_type") || string.IsNullOrWhiteSpace(authData["grant_type"]))
        {
            throw new ArgumentException(
                $"Target '{targetName}' OAuth configuration missing required field: 'grant_type'");
        }

        var grantType = authData["grant_type"].ToLowerInvariant();

        // Perform grant-type-specific validation
        switch (grantType)
        {
            case "password":
                ValidatePasswordGrant(targetName, authData);
                break;

            case "client_credentials":
                ValidateClientCredentialsGrant(targetName, authData);
                break;

            case "authorization_code":
                ValidateAuthorizationCodeGrant(targetName, authData);
                break;

            case "refresh_token":
                ValidateRefreshTokenGrant(targetName, authData);
                break;

            default:
                _logger.LogWarning(
                    "OAuthService: Unknown grant_type '{GrantType}' for target '{TargetName}'. " +
                    "Basic validation only - ensure all required fields are present.",
                    grantType, targetName);
                break;
        }
    }

    /// <summary>
    /// Validates configuration for OAuth 2.0 Password Grant (Resource Owner Password Credentials).
    /// </summary>
    private void ValidatePasswordGrant(string targetName, Dictionary<string, string> authData)
    {
        var requiredFields = new[] { "username", "password", "client_id" };

        foreach (var field in requiredFields)
        {
            if (!authData.ContainsKey(field) || string.IsNullOrWhiteSpace(authData[field]))
            {
                throw new ArgumentException(
                    $"Target '{targetName}' with grant_type='password' missing required field: '{field}'");
            }
        }

        // client_secret is optional for password grant (public clients)
        if (authData.ContainsKey("client_secret") && string.IsNullOrWhiteSpace(authData["client_secret"]))
        {
            _logger.LogWarning(
                "OAuthService: Target '{TargetName}' has empty client_secret - treating as public client",
                targetName);
        }
    }

    /// <summary>
    /// Validates configuration for OAuth 2.0 Client Credentials Grant.
    /// </summary>
    private void ValidateClientCredentialsGrant(string targetName, Dictionary<string, string> authData)
    {
        var requiredFields = new[] { "client_id", "client_secret" };

        foreach (var field in requiredFields)
        {
            if (!authData.ContainsKey(field) || string.IsNullOrWhiteSpace(authData[field]))
            {
                throw new ArgumentException(
                    $"Target '{targetName}' with grant_type='client_credentials' missing required field: '{field}'");
            }
        }
    }

    /// <summary>
    /// Validates configuration for OAuth 2.0 Authorization Code Grant.
    /// </summary>
    private void ValidateAuthorizationCodeGrant(string targetName, Dictionary<string, string> authData)
    {
        var requiredFields = new[] { "client_id", "client_secret", "code", "redirect_uri" };

        foreach (var field in requiredFields)
        {
            if (!authData.ContainsKey(field) || string.IsNullOrWhiteSpace(authData[field]))
            {
                throw new ArgumentException(
                    $"Target '{targetName}' with grant_type='authorization_code' missing required field: '{field}'");
            }
        }
    }

    /// <summary>
    /// Validates configuration for OAuth 2.0 Refresh Token Grant.
    /// </summary>
    private void ValidateRefreshTokenGrant(string targetName, Dictionary<string, string> authData)
    {
        var requiredFields = new[] { "client_id", "refresh_token" };

        foreach (var field in requiredFields)
        {
            if (!authData.ContainsKey(field) || string.IsNullOrWhiteSpace(authData[field]))
            {
                throw new ArgumentException(
                    $"Target '{targetName}' with grant_type='refresh_token' missing required field: '{field}'");
            }
        }

        // client_secret is typically required but may be optional for public clients
        if (!authData.ContainsKey("client_secret") || string.IsNullOrWhiteSpace(authData["client_secret"]))
        {
            _logger.LogWarning(
                "OAuthService: Target '{TargetName}' using refresh_token grant without client_secret - treating as public client",
                targetName);
        }
    }

    private Dictionary<string, string> BuildTokenRequestFormData(Dictionary<string, string> authData)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = authData["grant_type"]
        };

        var grantType = authData["grant_type"].ToLowerInvariant();

        if (grantType == "password")
        {
            formData["username"] = authData["username"];
            formData["password"] = authData["password"];
            formData["client_id"] = authData["client_id"];

            if (authData.TryGetValue("scope", out var scope) && !string.IsNullOrWhiteSpace(scope))
            {
                formData["scope"] = scope;
            }

            // For password grant type, client_secret goes in form body if provided
            if (authData.TryGetValue("client_secret", out var clientSecret) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                formData["client_secret"] = clientSecret;
            }
        }
        // Future: Add support for other grant types here (client_credentials, authorization_code, etc.)

        return formData;
    }

    public void ClearTokenCache(string targetName)
    {
        if (_tokenCache.TryRemove(targetName, out _))
        {
            _logger.LogInformation("OAuthService: Cleared token cache for target '{TargetName}'", targetName);
        }
    }

    public void ClearAllTokens()
    {
        var count = _tokenCache.Count;
        _tokenCache.Clear();
        _logger.LogInformation("OAuthService: Cleared all cached tokens ({Count} tokens removed)", count);
    }

    public Dictionary<string, object> GetCacheStatistics()
    {
        return new Dictionary<string, object>
        {
            ["cachedTokenCount"] = _tokenCache.Count,
            ["cacheHits"] = _cacheHits,
            ["cacheMisses"] = _cacheMisses,
            ["tokenAcquisitions"] = _tokenAcquisitions,
            ["tokenRefreshes"] = _tokenRefreshes,
            ["tokenAcquisitionFailures"] = _tokenAcquisitionFailures,
            ["cacheHitRate"] = _cacheHits + _cacheMisses > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses) * 100
                : 0.0,
            ["cachedTargets"] = _tokenCache.Keys.ToList()
        };
    }

    /// <summary>
    /// Sanitizes OAuth response content for logging by removing sensitive fields.
    /// </summary>
    private static string SanitizeOAuthResponseForLogging(string responseContent)
    {
        if (string.IsNullOrEmpty(responseContent))
            return responseContent;

        try
        {
            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
            if (json == null)
                return "[unparseable response]";

            // List of sensitive fields to redact
            var sensitiveFields = new[] { "access_token", "refresh_token", "id_token", "token", "secret", "password", "client_secret" };
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

            return JsonSerializer.Serialize(sanitized);
        }
        catch
        {
            // If we can't parse it as JSON, return a generic message
            return "[response content redacted]";
        }
    }
}
