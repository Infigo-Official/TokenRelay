using System.Text.RegularExpressions;
using System.Web;

namespace TokenRelay.Utilities;

/// <summary>
/// Helper class for resolving variable placeholders from configuration.
/// Variables act as a lookup table — values are injected when the incoming request
/// references them via {name} syntax in query strings, or {{name}} syntax in JSON bodies.
/// </summary>
public static partial class VariablesHelper
{
    [GeneratedRegex(@"^\{(\w+)\}$")]
    private static partial Regex StandalonePlaceholderRegex();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex ValuePlaceholderRegex();

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex BodyPlaceholderRegex();

    /// <summary>
    /// Resolves variable placeholders in the request query string using configured values.
    /// </summary>
    /// <param name="baseUrl">The target URL (may already include query string)</param>
    /// <param name="variables">Variables from target configuration (lookup table)</param>
    /// <param name="requestQueryString">Query string from incoming request (e.g., "?{script}&amp;name=foo")</param>
    /// <returns>Tuple of (resolved URL, error message if any placeholder is unknown)</returns>
    public static (string url, string? error) ResolveVariablePlaceholders(
        string baseUrl,
        Dictionary<string, string>? variables,
        string? requestQueryString)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return (baseUrl, null);

        // If no request query string, return base URL unchanged (no placeholders to resolve)
        if (string.IsNullOrEmpty(requestQueryString))
            return (baseUrl, null);

        // Strip leading '?' if present and decode URL-encoded characters
        // (e.g., %7B/%7D → {/}) so that placeholder regex can match
        var qs = requestQueryString!.StartsWith('?')
            ? requestQueryString[1..]
            : requestQueryString;
        qs = Uri.UnescapeDataString(qs);

        if (string.IsNullOrEmpty(qs))
            return (baseUrl, null);

        // Split into individual key=value (or key-only) segments
        var segments = qs.Split('&');
        var resolvedParts = new List<string>();
        var standaloneMatcher = StandalonePlaceholderRegex();
        var valueMatcher = ValuePlaceholderRegex();

        foreach (var segment in segments)
        {
            var eqIndex = segment.IndexOf('=');

            if (eqIndex < 0)
            {
                // Key-only segment, e.g. "{script}" or "plain"
                var standaloneMatch = standaloneMatcher.Match(segment);
                if (standaloneMatch.Success)
                {
                    var name = standaloneMatch.Groups[1].Value;
                    if (variables == null || !variables.TryGetValue(name, out var configValue))
                        return (baseUrl, $"Unknown query parameter placeholder: {name}");

                    resolvedParts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(configValue)}");
                }
                else
                {
                    // No placeholder, pass through as-is
                    resolvedParts.Add(segment);
                }
            }
            else
            {
                // key=value segment
                var key = segment[..eqIndex];
                var value = segment[(eqIndex + 1)..];

                // Check if key itself is a placeholder
                var keyMatch = standaloneMatcher.Match(key);
                if (keyMatch.Success)
                {
                    var name = keyMatch.Groups[1].Value;
                    if (variables == null || !variables.TryGetValue(name, out var configValue))
                        return (baseUrl, $"Unknown query parameter placeholder: {name}");

                    resolvedParts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(configValue)}");
                    continue;
                }

                // Check if value contains placeholders
                if (valueMatcher.IsMatch(value))
                {
                    var resolvedValue = valueMatcher.Replace(value, match =>
                    {
                        var name = match.Groups[1].Value;
                        if (variables == null || !variables.TryGetValue(name, out var configValue))
                            return $"\0ERROR:{name}";

                        return Uri.EscapeDataString(configValue);
                    });

                    // Check if any placeholder was unresolved
                    var errorMatch = Regex.Match(resolvedValue, @"\0ERROR:(\w+)");
                    if (errorMatch.Success)
                        return (baseUrl, $"Unknown query parameter placeholder: {errorMatch.Groups[1].Value}");

                    resolvedParts.Add($"{key}={resolvedValue}");
                }
                else
                {
                    // No placeholders, pass through unchanged
                    resolvedParts.Add(segment);
                }
            }
        }

        var resolvedQs = string.Join("&", resolvedParts);

        // Append to base URL
        if (baseUrl.Contains('?'))
            return ($"{baseUrl}&{resolvedQs}", null);

        return ($"{baseUrl}?{resolvedQs}", null);
    }

    /// <summary>
    /// Resolves {{variableName}} placeholders in a JSON request body using configured variables.
    /// Unknown placeholders are left as-is (not an error).
    /// Single-brace {name} placeholders are NOT matched.
    /// </summary>
    /// <param name="body">The JSON body string</param>
    /// <param name="variables">Variables from target configuration</param>
    /// <returns>The body with known placeholders replaced</returns>
    public static string ResolveBodyPlaceholders(string? body, Dictionary<string, string>? variables)
    {
        if (string.IsNullOrEmpty(body) || variables == null || variables.Count == 0)
            return body ?? string.Empty;

        return BodyPlaceholderRegex().Replace(body, match =>
        {
            var name = match.Groups[1].Value;
            return variables.TryGetValue(name, out var value) ? value : match.Value;
        });
    }
}
