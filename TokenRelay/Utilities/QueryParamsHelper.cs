using System.Web;

namespace TokenRelay.Utilities;

/// <summary>
/// Helper class for merging query parameters from configuration and incoming requests.
/// </summary>
public static class QueryParamsHelper
{
    /// <summary>
    /// Merges configured query parameters with the request URL.
    /// Request query params take precedence over configured params (for overriding).
    /// </summary>
    /// <param name="baseUrl">The target URL (may already include query string)</param>
    /// <param name="configuredParams">Query params from target configuration</param>
    /// <param name="requestQueryString">Query string from incoming request (e.g., "?foo=bar")</param>
    /// <returns>URL with merged query parameters</returns>
    public static string MergeQueryParams(
        string baseUrl,
        Dictionary<string, string>? configuredParams,
        string? requestQueryString)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return baseUrl;

        // If no params to merge, return original URL
        var hasConfiguredParams = configuredParams != null && configuredParams.Count > 0;
        var hasRequestParams = !string.IsNullOrEmpty(requestQueryString);

        if (!hasConfiguredParams && !hasRequestParams)
            return baseUrl;

        var uriBuilder = new UriBuilder(baseUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);

        // 1. Add configured params first (lower priority - can be overridden)
        if (hasConfiguredParams)
        {
            foreach (var param in configuredParams!)
            {
                if (!string.IsNullOrEmpty(param.Value))
                {
                    query[param.Key] = param.Value;
                }
            }
        }

        // 2. Add/override with request params (higher priority)
        if (hasRequestParams)
        {
            var requestParams = HttpUtility.ParseQueryString(requestQueryString!);
            foreach (string? key in requestParams.AllKeys)
            {
                if (key != null)
                {
                    query[key] = requestParams[key];
                }
            }
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    /// <summary>
    /// Checks if the target has any configured query parameters.
    /// </summary>
    /// <param name="queryParams">The query parameters dictionary</param>
    /// <returns>True if there are any non-empty query parameters</returns>
    public static bool HasQueryParams(Dictionary<string, string>? queryParams)
    {
        return queryParams != null && queryParams.Any(p => !string.IsNullOrEmpty(p.Value));
    }
}
