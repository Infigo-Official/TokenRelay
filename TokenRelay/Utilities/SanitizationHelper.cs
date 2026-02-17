using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace TokenRelay.Utilities;

/// <summary>
/// Centralized utility class for sanitizing sensitive data before logging.
/// Provides consistent redaction of headers, body content, and other sensitive information.
/// </summary>
public static class SanitizationHelper
{
    /// <summary>
    /// List of HTTP headers that contain sensitive information and should be redacted.
    /// </summary>
    public static readonly string[] SensitiveHeaders =
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

    /// <summary>
    /// List of JSON/form field names that contain sensitive information and should be redacted.
    /// </summary>
    public static readonly string[] SensitiveFields =
    {
        "access_token",
        "refresh_token",
        "id_token",
        "token",
        "secret",
        "password",
        "client_secret",
        "api_key",
        "apikey",
        "authorization",
        "credential",
        "credentials",
        "private_key",
        "privatekey",
        // OAuth1 specific fields
        "consumer_key",
        "consumer_secret",
        "token_id",
        "token_secret",
        "oauth_signature",
        "oauth_consumer_key",
        "oauth_token"
    };

    /// <summary>
    /// Sanitizes header values to prevent logging sensitive information.
    /// </summary>
    /// <param name="headerName">The name of the header.</param>
    /// <param name="value">The value of the header.</param>
    /// <param name="truncateLength">Optional length to truncate non-sensitive values (0 = no truncation).</param>
    /// <returns>The sanitized header value.</returns>
    public static string SanitizeHeaderValue(string headerName, string value, int truncateLength = 0)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        if (SensitiveHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase))
        {
            return "[REDACTED]";
        }

        // Optionally truncate long values to prevent log bloat
        if (truncateLength > 0 && value.Length > truncateLength)
        {
            return $"{value[..truncateLength]}...";
        }

        return value;
    }

    /// <summary>
    /// Sanitizes body content to prevent logging sensitive information like tokens and credentials.
    /// Supports JSON and form-urlencoded content with recursive sanitization for nested objects.
    /// </summary>
    /// <param name="content">The body content to sanitize.</param>
    /// <param name="contentType">The content type of the body.</param>
    /// <returns>The sanitized body content.</returns>
    public static string SanitizeBodyContent(string content, string contentType)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        // For JSON content, try to redact sensitive fields recursively
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeJsonContent(content);
        }

        // For form-urlencoded content, redact sensitive parameters
        if (contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeFormUrlEncodedContent(content);
        }

        return content;
    }

    /// <summary>
    /// Sanitizes JSON content by recursively redacting sensitive fields.
    /// </summary>
    /// <param name="jsonContent">The JSON content to sanitize.</param>
    /// <returns>The sanitized JSON content.</returns>
    public static string SanitizeJsonContent(string jsonContent)
    {
        if (string.IsNullOrEmpty(jsonContent))
            return jsonContent ?? string.Empty;

        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            var sanitized = SanitizeJsonElement(document.RootElement);
            return JsonSerializer.Serialize(sanitized);
        }
        catch
        {
            // If JSON parsing fails, return a generic message to avoid leaking data
            return "[JSON content - parsing failed]";
        }
    }

    /// <summary>
    /// Recursively sanitizes a JSON element, handling objects, arrays, and primitive values.
    /// </summary>
    private static object? SanitizeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var sanitizedObject = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    if (IsSensitiveFieldName(property.Name))
                    {
                        sanitizedObject[property.Name] = "[REDACTED]";
                    }
                    else
                    {
                        sanitizedObject[property.Name] = SanitizeJsonElement(property.Value);
                    }
                }
                return sanitizedObject;

            case JsonValueKind.Array:
                var sanitizedArray = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    sanitizedArray.Add(SanitizeJsonElement(item));
                }
                return sanitizedArray;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                    return longValue;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    /// <summary>
    /// Checks if a field name is considered sensitive.
    /// </summary>
    private static bool IsSensitiveFieldName(string fieldName)
    {
        return SensitiveFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sanitizes form-urlencoded content by redacting sensitive parameters.
    /// </summary>
    private static string SanitizeFormUrlEncodedContent(string content)
    {
        var parts = content.Split('&');
        var sanitizedParts = new List<string>();

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2 && IsSensitiveFieldName(keyValue[0]))
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

    /// <summary>
    /// Serializes request headers as a JSON string for NewRelic telemetry attributes.
    /// Sensitive header values are redacted via <see cref="SanitizeHeaderValue"/>.
    /// The result is capped at 4000 characters (NewRelic custom attribute limit).
    /// </summary>
    /// <param name="headers">The HTTP request headers to serialize.</param>
    /// <returns>A JSON string of sanitized header key-value pairs.</returns>
    public static string SerializeHeadersForTelemetry(IHeaderDictionary headers)
    {
        if (headers == null || headers.Count == 0)
            return "{}";

        var sanitized = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            var value = string.Join(", ", header.Value.ToArray());
            sanitized[header.Key] = SanitizeHeaderValue(header.Key, value);
        }

        var json = JsonSerializer.Serialize(sanitized);
        if (json.Length > 4000)
        {
            json = json[..3997] + "...";
        }
        return json;
    }

    /// <summary>
    /// Serializes request headers from a dictionary (as passed via plugin parameters) for NewRelic telemetry.
    /// Sensitive header values are redacted via <see cref="SanitizeHeaderValue"/>.
    /// The result is capped at 4000 characters (NewRelic custom attribute limit).
    /// </summary>
    /// <param name="headers">The headers dictionary to serialize.</param>
    /// <returns>A JSON string of sanitized header key-value pairs.</returns>
    public static string SerializeHeadersForTelemetry(Dictionary<string, string> headers)
    {
        if (headers == null || headers.Count == 0)
            return "{}";

        var sanitized = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            sanitized[header.Key] = SanitizeHeaderValue(header.Key, header.Value);
        }

        var json = JsonSerializer.Serialize(sanitized);
        if (json.Length > 4000)
        {
            json = json[..3997] + "...";
        }
        return json;
    }

    /// <summary>
    /// Sanitizes a URL for logging by removing query string parameters that might contain sensitive data.
    /// </summary>
    /// <param name="url">The URL to sanitize.</param>
    /// <returns>The URL without query string parameters.</returns>
    public static string SanitizeUrlForLogging(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url ?? string.Empty;

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
    /// Sanitizes user-provided strings for logging to prevent log injection attacks.
    /// Removes all ASCII control characters (0x00-0x1F and 0x7F) that could be used
    /// to inject fake log entries or manipulate log output.
    /// </summary>
    /// <param name="input">The input string to sanitize.</param>
    /// <returns>The sanitized string safe for logging.</returns>
    public static string SanitizeForLogging(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        // Remove all ASCII control characters that could be used for log injection
        // This includes: NUL, SOH, STX, ETX, EOT, ENQ, ACK, BEL, BS, TAB, LF, VT, FF, CR,
        // SO, SI, DLE, DC1-DC4, NAK, SYN, ETB, CAN, EM, SUB, ESC, FS, GS, RS, US, and DEL
        var result = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c < 0x20 || c == 0x7F)
            {
                // Replace control characters with space for tabs, empty for others
                if (c == '\t')
                    result.Append(' ');
                // Skip other control characters entirely
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
