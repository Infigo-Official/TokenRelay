using TokenRelay.Utilities;

namespace TokenRelay.Plugins;

public class DownloaderPlugin : ITokenRelayPlugin
{
    public string Name => "Downloader";
    public string Version => "1.0.0";

    private HttpClient? _httpClient;
    private int _timeoutSeconds = 30;
    private HashSet<string>? _allowedHosts;

    public void Configure(Dictionary<string, string> settings)
    {
        if (settings.TryGetValue("timeoutSeconds", out var timeoutStr) && int.TryParse(timeoutStr, out var timeout))
        {
            _timeoutSeconds = timeout;
        }

        if (settings.TryGetValue("allowedHosts", out var hostsStr) && !string.IsNullOrWhiteSpace(hostsStr))
        {
            _allowedHosts = new HashSet<string>(
                hostsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Dictionary<string, object>> Execute(string function, Dictionary<string, object> parameters)
    {
        return function.ToLowerInvariant() switch
        {
            "fetch" => await FetchUrl(parameters),
            _ => throw new NotSupportedException($"Function '{function}' is not supported by {Name} plugin")
        };
    }

    private async Task<Dictionary<string, object>> FetchUrl(Dictionary<string, object> parameters)
    {
        var transaction = NewRelic.Api.Agent.NewRelic.GetAgent()?.CurrentTransaction;
        string? url = null;
        Uri? uri = null;

        // Serialize incoming request headers for NewRelic if provided by FunctionController
        if (parameters.TryGetValue("__requestHeaders", out var headersObj) && headersObj is Dictionary<string, string> reqHeaders)
        {
            transaction?.AddCustomAttribute("tokenrelay.downloader.headers", SanitizationHelper.SerializeHeadersForTelemetry(reqHeaders));
        }

        try
        {
            if (_httpClient == null)
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "ValidationError");
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "HttpClient not configured for Downloader plugin",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (!parameters.TryGetValue("url", out var urlObj) || urlObj is not string urlStr || string.IsNullOrWhiteSpace(urlStr))
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "ValidationError");
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "Parameter 'url' is required",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            url = urlStr;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.url", SanitizationHelper.SanitizeForLogging(url));
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "ValidationError");
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Invalid URL format: '{url}'",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            transaction?.AddCustomAttribute("tokenrelay.downloader.url", SanitizationHelper.SanitizeForLogging(url));
            transaction?.AddCustomAttribute("tokenrelay.downloader.host", uri.Host);

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "ValidationError");
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Unsupported URL scheme: '{uri.Scheme}'. Only 'http' and 'https' are allowed",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (_allowedHosts != null && !_allowedHosts.Contains(uri.Host))
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "ValidationError");
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Host '{uri.Host}' is not in the allowed hosts list",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));

            var fetchStopwatch = ValueStopwatch.StartNew();
            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var fetchElapsedMs = fetchStopwatch.GetElapsedMilliseconds();

            transaction?.AddCustomAttribute("tokenrelay.downloader.status_code", (int)response.StatusCode);
            transaction?.AddCustomAttribute("tokenrelay.downloader.response_time_ms", fetchElapsedMs);

            if (!response.IsSuccessStatusCode)
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
                response.Dispose();
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Remote server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    ["statusCode"] = (int)response.StatusCode,
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var contentLength = response.Content.Headers.ContentLength;
            var contentDisposition = response.Content.Headers.ContentDisposition?.FileName;

            transaction?.AddCustomAttribute("tokenrelay.downloader.content_type", contentType);
            if (contentLength.HasValue)
            {
                transaction?.AddCustomAttribute("tokenrelay.downloader.content_length", contentLength.Value);
            }
            transaction?.AddCustomAttribute("tokenrelay.downloader.success", true);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);

            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["__responseStream"] = stream,
                ["__responseContentType"] = contentType,
                ["__httpResponse"] = response
            };

            if (contentLength.HasValue)
            {
                result["__responseContentLength"] = contentLength.Value;
            }

            if (!string.IsNullOrEmpty(contentDisposition))
            {
                result["__responseFileName"] = contentDisposition;
            }

            return result;
        }
        catch (TaskCanceledException ex)
        {
            transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
            transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "Timeout");
            NewRelic.Api.Agent.NewRelic.NoticeError(ex, new Dictionary<string, object>
            {
                { "tokenrelay.downloader.url", SanitizationHelper.SanitizeForLogging(url ?? "") },
                { "tokenrelay.downloader.error_type", "Timeout" },
                { "tokenrelay.downloader.timeout_seconds", _timeoutSeconds }
            });
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = $"Request timed out after {_timeoutSeconds} seconds",
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
        catch (HttpRequestException ex)
        {
            transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
            transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "HttpRequestException");
            NewRelic.Api.Agent.NewRelic.NoticeError(ex, new Dictionary<string, object>
            {
                { "tokenrelay.downloader.url", SanitizationHelper.SanitizeForLogging(url ?? "") },
                { "tokenrelay.downloader.error_type", "HttpRequestException" }
            });
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = $"HTTP request failed: {ex.Message}",
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
        catch (Exception ex)
        {
            transaction?.AddCustomAttribute("tokenrelay.downloader.success", false);
            transaction?.AddCustomAttribute("tokenrelay.downloader.error_type", "Unexpected");
            NewRelic.Api.Agent.NewRelic.NoticeError(ex, new Dictionary<string, object>
            {
                { "tokenrelay.downloader.url", SanitizationHelper.SanitizeForLogging(url ?? "") },
                { "tokenrelay.downloader.error_type", "Unexpected" }
            });
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
    }
}
