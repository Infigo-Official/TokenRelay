using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TokenRelay.Services;
using TokenRelay.Utilities;

namespace TokenRelay.Controllers;

[ApiController]
[Route("function")]
public class FunctionController : ControllerBase
{
    private readonly IPluginService _pluginService;
    private readonly ILogger<FunctionController> _logger;

    public FunctionController(IPluginService pluginService, ILogger<FunctionController> logger)
    {
        _pluginService = pluginService;
        _logger = logger;
    }

    [HttpPost("{plugin}/{function}")]
    public Task<IActionResult> ExecuteFunction(string plugin, string function)
        => ExecuteFunctionInternal(plugin, function);

    [HttpGet("{plugin}/{function}")]
    public Task<IActionResult> ExecuteFunctionGet(string plugin, string function)
        => ExecuteFunctionInternal(plugin, function);

    private async Task<IActionResult> ExecuteFunctionInternal(
        string plugin,
        string function)
    {
        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Sanitize route parameters to prevent log injection via URL-encoded newlines
        var sanitizedPlugin = SanitizationHelper.SanitizeForLogging(plugin);
        var sanitizedFunction = SanitizationHelper.SanitizeForLogging(function);

        _logger.LogInformation("FunctionController: Received request to execute function '{Function}' in plugin '{Plugin}' from {ClientIP}",
            sanitizedFunction, sanitizedPlugin, clientIP);

        Stream? fileStream = null;
        bool responseStreamReturned = false;
        try
        {
            if (string.IsNullOrEmpty(plugin))
            {
                _logger.LogWarning("FunctionController: Plugin name is required for function execution from {ClientIP}", clientIP);
                return BadRequest("Plugin name is required");
            }
            if (string.IsNullOrEmpty(function))
            {
                _logger.LogWarning("FunctionController: Function name is required for plugin '{Plugin}' from {ClientIP}", sanitizedPlugin, clientIP);
                return BadRequest("Function name is required");
            }

            _logger.LogDebug("FunctionController: Processing parameters for '{Plugin}.{Function}' from {ClientIP}",
                sanitizedPlugin, sanitizedFunction, clientIP);

            var parameters = new Dictionary<string, object>();

            // Add query parameters
            var queryParamCount = 0;
            foreach (var queryParam in Request.Query)
            {
                parameters[queryParam.Key] = queryParam.Value.ToString();
                queryParamCount++;
            }

            if (queryParamCount > 0)
            {
                _logger.LogDebug("FunctionController: Added {QueryParamCount} query parameters", queryParamCount);
            }

            // Handle file upload and form data
            if (Request.HasFormContentType)
            {
                _logger.LogDebug("FunctionController: Processing form data for '{Plugin}.{Function}'", sanitizedPlugin, sanitizedFunction);
                var form = await Request.ReadFormAsync();

                // Add form fields as parameters
                var formFieldCount = 0;
                foreach (var field in form)
                {
                    if (field.Key != "file") // Don't include file field as regular parameter
                    {
                        parameters[field.Key] = field.Value.ToString();
                        formFieldCount++;
                    }
                }

                if (formFieldCount > 0)
                {
                    _logger.LogDebug("FunctionController: Added {FormFieldCount} form field parameters", formFieldCount);
                }

                // Handle file upload
                var file = form.Files.FirstOrDefault();
                if (file != null && file.Length > 0)
                {
                    _logger.LogInformation("FunctionController: Processing file upload '{FileName}' - Size: {FileSize} bytes, ContentType: {ContentType}",
                        file.FileName, file.Length, file.ContentType);

                    // For large files (>50MB), use a temporary file, otherwise keep in memory
                    const long maxMemorySize = 50 * 1024 * 1024; // 50MB

                    if (file.Length > maxMemorySize)
                    {
                        // Use temporary file for large uploads
                        var tempFilePath = Path.GetTempFileName();
                        _logger.LogDebug("FunctionController: Using temporary file for large upload: {TempFile}", tempFilePath);

                        using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await file.CopyToAsync(tempFileStream);
                        }

                        fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                    }
                    else
                    {
                        // Keep smaller files in memory
                        _logger.LogDebug("FunctionController: Keeping file in memory for processing");
                        var memoryStream = new MemoryStream();
                        await file.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;
                        fileStream = memoryStream;
                    }

                    // Add file-related parameters
                    parameters["__fileStream"] = fileStream;
                    parameters["__fileName"] = file.FileName ?? "unknown";
                    parameters["__fileSize"] = file.Length;
                    parameters["__contentType"] = file.ContentType ?? "application/octet-stream";

                    _logger.LogDebug("FunctionController: Added file parameters to function call");
                }
                else
                {
                    _logger.LogDebug("FunctionController: No file found in form data");
                }
            }
            else if (Request.ContentType?.StartsWith("application/json") == true)
            {
                _logger.LogDebug("FunctionController: Processing JSON body for '{Plugin}.{Function}'", sanitizedPlugin, sanitizedFunction);

                // Handle JSON body
                using var body = await JsonDocument.ParseAsync(Request.Body);
                if (body.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var jsonParamCount = 0;
                    foreach (var property in body.RootElement.EnumerateObject())
                    {
                        object value = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? "",
                            JsonValueKind.Number => property.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null!,
                            _ => property.Value.ToString()
                        };
                        parameters[property.Name] = value;
                        jsonParamCount++;
                    }

                    if (jsonParamCount > 0)
                    {
                        _logger.LogDebug("FunctionController: Added {JsonParamCount} JSON parameters", jsonParamCount);
                    }
                }
            }

            _logger.LogInformation("FunctionController: Executing function '{Function}' on plugin '{Plugin}' with {ParameterCount} parameters from {ClientIP}",
                sanitizedFunction, sanitizedPlugin, parameters.Count, clientIP);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _pluginService.ExecutePluginFunctionAsync(plugin, function, parameters);
            stopwatch.Stop();

            _logger.LogInformation("FunctionController: Function '{Plugin}.{Function}' executed successfully in {ElapsedMs}ms from {ClientIP}",
                sanitizedPlugin, sanitizedFunction, stopwatch.ElapsedMilliseconds, clientIP);

            // Check if the plugin returned a response stream (e.g. file proxy)
            if (result.TryGetValue("__responseStream", out var streamObj) && streamObj is Stream responseStream)
            {
                var contentType = result.TryGetValue("__responseContentType", out var ctObj) && ctObj is string ct
                    ? ct : "application/octet-stream";
                var fileName = result.TryGetValue("__responseFileName", out var fnObj) && fnObj is string fn
                    ? fn : null;

                _logger.LogInformation("FunctionController: Returning stream response for '{Plugin}.{Function}' with ContentType={ContentType} from {ClientIP}",
                    sanitizedPlugin, sanitizedFunction, contentType, clientIP);

                // Register the HttpResponseMessage for disposal after the response completes
                if (result.TryGetValue("__httpResponse", out var httpRespObj) && httpRespObj is IDisposable httpResp)
                {
                    HttpContext.Response.RegisterForDispose(httpResp);
                }

                responseStreamReturned = true;
                return File(responseStream, contentType, fileName);
            }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "FunctionController: Invalid plugin or function request for '{Plugin}.{Function}' from {ClientIP}",
                sanitizedPlugin, sanitizedFunction, clientIP);
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FunctionController: Error executing plugin function '{Plugin}.{Function}' from {ClientIP}",
                sanitizedPlugin, sanitizedFunction, clientIP);
            return StatusCode(500, new
            {
                success = false,
                error = "Internal server error while executing function",
                data = new Dictionary<string, object>()
            });
        }
        finally
        {
            // Ensure file stream is properly disposed (but not response streams - ASP.NET manages those)
            if (fileStream != null && !responseStreamReturned)
            {
                _logger.LogDebug("FunctionController: Disposing file stream for '{Plugin}.{Function}'", sanitizedPlugin, sanitizedFunction);
                fileStream.Dispose();
            }
        }
    }
}
