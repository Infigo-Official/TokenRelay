using System.Security.Cryptography;
using System.Text;
using System.Web;
using TokenRelay.Models;

namespace TokenRelay.Services;

/// <summary>
/// Service for generating OAuth 1.0 authentication headers.
/// Supports HMAC-SHA256 (preferred) and HMAC-SHA1 signature methods.
/// </summary>
public interface IOAuth1Service
{
    /// <summary>
    /// Generates the OAuth 1.0 Authorization header for a request.
    /// Unlike OAuth2, OAuth1 requires request-specific signature generation.
    /// </summary>
    /// <param name="targetName">The target name for logging purposes</param>
    /// <param name="target">The target configuration containing OAuth1 credentials</param>
    /// <param name="httpMethod">The HTTP method (GET, POST, etc.)</param>
    /// <param name="requestUrl">The full request URL including query parameters</param>
    /// <param name="additionalParams">Optional additional parameters to include in signature</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The complete OAuth Authorization header value</returns>
    Task<string> GenerateAuthorizationHeaderAsync(
        string targetName,
        TargetConfig target,
        HttpMethod httpMethod,
        string requestUrl,
        Dictionary<string, string>? additionalParams = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates OAuth1 configuration for a target.
    /// </summary>
    /// <param name="targetName">The target name for error messages</param>
    /// <param name="target">The target configuration to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid</exception>
    void ValidateConfiguration(string targetName, TargetConfig target);

    /// <summary>
    /// Gets service statistics for monitoring.
    /// </summary>
    Dictionary<string, object> GetStatistics();
}

/// <summary>
/// Implementation of OAuth 1.0 signature generation following RFC 5849.
/// Designed to be generic and reusable for any OAuth1 API (NetSuite, Twitter, etc.)
/// </summary>
public class OAuth1Service : IOAuth1Service
{
    private readonly ILogger<OAuth1Service> _logger;

    // Statistics counters
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;

    // OAuth1 constants
    private const string OAuthVersion = "1.0";
    private const string DefaultSignatureMethod = "HMAC-SHA256";

    // Required configuration keys
    private static readonly string[] RequiredKeys =
    {
        "consumer_key",
        "consumer_secret",
        "token_id",
        "token_secret",
        "realm"
    };

    public OAuth1Service(ILogger<OAuth1Service> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> GenerateAuthorizationHeaderAsync(
        string targetName,
        TargetConfig target,
        HttpMethod httpMethod,
        string requestUrl,
        Dictionary<string, string>? additionalParams = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalRequests);

        try
        {
            // Validate configuration
            ValidateConfiguration(targetName, target);

            // Extract credentials from authData
            var consumerKey = target.AuthData["consumer_key"];
            var consumerSecret = target.AuthData["consumer_secret"];
            var tokenId = target.AuthData["token_id"];
            var tokenSecret = target.AuthData["token_secret"];
            var realm = target.AuthData["realm"];
            var signatureMethod = target.AuthData.TryGetValue("signature_method", out var method)
                ? method
                : DefaultSignatureMethod;

            // Generate OAuth parameters
            var timestamp = GenerateTimestamp();
            var nonce = GenerateNonce();

            // Build OAuth parameters dictionary
            var oauthParams = new Dictionary<string, string>
            {
                ["oauth_consumer_key"] = consumerKey,
                ["oauth_token"] = tokenId,
                ["oauth_signature_method"] = signatureMethod,
                ["oauth_timestamp"] = timestamp,
                ["oauth_nonce"] = nonce,
                ["oauth_version"] = OAuthVersion
            };

            // Parse the URL to get base URL and query parameters
            var uri = new Uri(requestUrl);
            var normalizedUrl = NormalizeRequestUrl(uri);
            var queryParams = ParseQueryString(uri.Query);

            // Collect all parameters for signature (oauth params + query params + additional params)
            var allParams = new Dictionary<string, string>(oauthParams);
            foreach (var param in queryParams)
            {
                allParams[param.Key] = param.Value;
            }
            if (additionalParams != null)
            {
                foreach (var param in additionalParams)
                {
                    allParams[param.Key] = param.Value;
                }
            }

            // Generate signature base string
            var signatureBaseString = GenerateSignatureBaseString(
                httpMethod.Method.ToUpperInvariant(),
                normalizedUrl,
                allParams);

            _logger.LogDebug("OAuth1Service: Generated signature base string for target '{TargetName}'", targetName);

            // Generate signature
            var signature = GenerateSignature(
                signatureBaseString,
                consumerSecret,
                tokenSecret,
                signatureMethod);

            // Build Authorization header
            var authHeader = BuildAuthorizationHeader(oauthParams, signature, realm);

            Interlocked.Increment(ref _successfulRequests);
            _logger.LogDebug("OAuth1Service: Successfully generated OAuth1 header for target '{TargetName}'", targetName);

            return Task.FromResult(authHeader);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);
            _logger.LogError(ex, "OAuth1Service: Failed to generate OAuth1 header for target '{TargetName}'", targetName);
            throw;
        }
    }

    /// <inheritdoc />
    public void ValidateConfiguration(string targetName, TargetConfig target)
    {
        if (target.AuthData == null || target.AuthData.Count == 0)
        {
            throw new InvalidOperationException(
                $"OAuth1 configuration missing for target '{targetName}'. AuthData dictionary is empty.");
        }

        var missingKeys = RequiredKeys
            .Where(key => !target.AuthData.ContainsKey(key) || string.IsNullOrWhiteSpace(target.AuthData[key]))
            .ToList();

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"OAuth1 configuration incomplete for target '{targetName}'. Missing required keys: {string.Join(", ", missingKeys)}");
        }

        // Validate signature method if specified
        if (target.AuthData.TryGetValue("signature_method", out var signatureMethod))
        {
            if (signatureMethod != "HMAC-SHA256" && signatureMethod != "HMAC-SHA1")
            {
                throw new InvalidOperationException(
                    $"Invalid signature_method '{signatureMethod}' for target '{targetName}'. " +
                    "Supported methods: HMAC-SHA256, HMAC-SHA1");
            }
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object> GetStatistics()
    {
        var total = Interlocked.Read(ref _totalRequests);
        var successful = Interlocked.Read(ref _successfulRequests);
        var failed = Interlocked.Read(ref _failedRequests);

        return new Dictionary<string, object>
        {
            ["totalRequests"] = total,
            ["successfulRequests"] = successful,
            ["failedRequests"] = failed,
            ["successRate"] = total > 0 ? (double)successful / total * 100 : 0.0
        };
    }

    /// <summary>
    /// Generates a Unix timestamp (seconds since epoch) for the current UTC time.
    /// </summary>
    public static string GenerateTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    }

    /// <summary>
    /// Generates a cryptographically secure random nonce with timestamp prefix for debugging.
    /// Format: {timestamp_ms}_{random_bytes}
    /// </summary>
    public static string GenerateNonce()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var randomPart = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"{timestamp}{randomPart}";
    }

    /// <summary>
    /// Normalizes the request URL per RFC 5849 Section 3.4.1.2.
    /// - Scheme and host are lowercase
    /// - Default ports (80 for http, 443 for https) are removed
    /// - Query string is removed (handled separately)
    /// </summary>
    public static string NormalizeRequestUrl(Uri uri)
    {
        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var port = uri.Port;
        var path = uri.AbsolutePath;

        // Remove default ports
        var includePort = !((scheme == "http" && port == 80) || (scheme == "https" && port == 443));

        var normalizedUrl = $"{scheme}://{host}";
        if (includePort)
        {
            normalizedUrl += $":{port}";
        }
        normalizedUrl += path;

        return normalizedUrl;
    }

    /// <summary>
    /// Generates the signature base string per RFC 5849 Section 3.4.1.
    /// Format: HTTP_METHOD&NORMALIZED_URL&NORMALIZED_PARAMS
    /// </summary>
    public static string GenerateSignatureBaseString(
        string httpMethod,
        string normalizedUrl,
        Dictionary<string, string> parameters)
    {
        // Sort parameters alphabetically by key, then by value
        var sortedParams = parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{PercentEncode(p.Key)}={PercentEncode(p.Value)}")
            .ToList();

        var normalizedParams = string.Join("&", sortedParams);

        return $"{httpMethod}&{PercentEncode(normalizedUrl)}&{PercentEncode(normalizedParams)}";
    }

    /// <summary>
    /// Generates the OAuth signature using the specified method.
    /// </summary>
    public static string GenerateSignature(
        string signatureBaseString,
        string consumerSecret,
        string tokenSecret,
        string signatureMethod)
    {
        // Construct the signing key: consumer_secret&token_secret
        var signingKey = $"{PercentEncode(consumerSecret)}&{PercentEncode(tokenSecret)}";
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        var dataBytes = Encoding.UTF8.GetBytes(signatureBaseString);

        byte[] hashBytes;

        if (signatureMethod == "HMAC-SHA256")
        {
            using var hmac = new HMACSHA256(keyBytes);
            hashBytes = hmac.ComputeHash(dataBytes);
        }
        else // HMAC-SHA1
        {
            using var hmac = new HMACSHA1(keyBytes);
            hashBytes = hmac.ComputeHash(dataBytes);
        }

        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Percent encodes a string per RFC 3986.
    /// </summary>
    public static string PercentEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Characters that should NOT be encoded per RFC 3986
        const string unreservedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~";

        var encoded = new StringBuilder(value.Length * 3);
        foreach (var c in value)
        {
            if (unreservedChars.Contains(c))
            {
                encoded.Append(c);
            }
            else
            {
                foreach (var b in Encoding.UTF8.GetBytes(c.ToString()))
                {
                    encoded.Append('%');
                    encoded.Append(b.ToString("X2"));
                }
            }
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Parses a query string into a dictionary.
    /// </summary>
    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(queryString))
            return result;

        var query = queryString.TrimStart('?');
        if (string.IsNullOrEmpty(query))
            return result;

        var parsed = HttpUtility.ParseQueryString(query);
        foreach (string? key in parsed.AllKeys)
        {
            if (key != null && parsed[key] != null)
            {
                result[key] = parsed[key]!;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the OAuth Authorization header string.
    /// </summary>
    private static string BuildAuthorizationHeader(
        Dictionary<string, string> oauthParams,
        string signature,
        string realm)
    {
        var headerParams = new List<string>
        {
            $"realm=\"{PercentEncode(realm)}\""
        };

        // Add oauth parameters in alphabetical order
        foreach (var param in oauthParams.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            headerParams.Add($"{PercentEncode(param.Key)}=\"{PercentEncode(param.Value)}\"");
        }

        // Add signature last
        headerParams.Add($"oauth_signature=\"{PercentEncode(signature)}\"");

        return "OAuth " + string.Join(", ", headerParams);
    }
}
