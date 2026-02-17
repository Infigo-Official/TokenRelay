using System.Text.RegularExpressions;
using System.Web;

namespace TokenRelay.Utilities;

/// <summary>
/// Helper class for resolving query parameter placeholders from configuration.
/// Configured query params act as a lookup table — values are only injected when
/// the incoming request explicitly references them via {paramName} syntax.
/// </summary>
public static partial class QueryParamsHelper
{
    [GeneratedRegex(@"^\{(\w+)\}$")]
    private static partial Regex StandalonePlaceholderRegex();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex ValuePlaceholderRegex();

    /// <summary>
    /// Resolves query parameter placeholders in the request query string using configured values.
    /// </summary>
    /// <param name="baseUrl">The target URL (may already include query string)</param>
    /// <param name="configuredParams">Query params from target configuration (lookup table)</param>
    /// <param name="requestQueryString">Query string from incoming request (e.g., "?{script}&amp;name=foo")</param>
    /// <returns>Tuple of (resolved URL, error message if any placeholder is unknown)</returns>
    public static (string url, string? error) ResolveQueryParamPlaceholders(
        string baseUrl,
        Dictionary<string, string>? configuredParams,
        string? requestQueryString)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return (baseUrl, null);

        // If no request query string, return base URL unchanged (no placeholders to resolve)
        if (string.IsNullOrEmpty(requestQueryString))
            return (baseUrl, null);

        // Strip leading '?' if present
        var qs = requestQueryString!.StartsWith('?')
            ? requestQueryString[1..]
            : requestQueryString;

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
                    if (configuredParams == null || !configuredParams.TryGetValue(name, out var configValue))
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
                    if (configuredParams == null || !configuredParams.TryGetValue(name, out var configValue))
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
                        if (configuredParams == null || !configuredParams.TryGetValue(name, out var configValue))
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
}
