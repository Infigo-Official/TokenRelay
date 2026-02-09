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
        try
        {
            if (!parameters.TryGetValue("url", out var urlObj) || urlObj is not string url || string.IsNullOrWhiteSpace(url))
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "Parameter 'url' is required",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Invalid URL format: '{url}'",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Unsupported URL scheme: '{uri.Scheme}'. Only 'http' and 'https' are allowed",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (_allowedHosts != null && !_allowedHosts.Contains(uri.Host))
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Host '{uri.Host}' is not in the allowed hosts list",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            if (_httpClient == null)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "HttpClient not configured for Downloader plugin",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
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
            var contentDisposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

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
        catch (TaskCanceledException)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = $"Request timed out after {_timeoutSeconds} seconds",
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
        catch (HttpRequestException ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = $"HTTP request failed: {ex.Message}",
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
        }
    }
}
