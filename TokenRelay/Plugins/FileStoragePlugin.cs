using System.Net;
using System.Text;

namespace TokenRelay.Plugins;

public class FileStoragePlugin : ITokenRelayPlugin
{
    public string Name => "FileStorage";
    public string Version => "1.0.0";

    private Dictionary<string, string> _destinations = new();
    private NetworkCredential? _credentials;

    public void Configure(Dictionary<string, string> settings)
    {
        // Parse destinations
        foreach (var setting in settings.Where(s => s.Key.StartsWith("destinations.")))
        {
            var key = setting.Key.Substring("destinations.".Length);
            _destinations[key] = setting.Value;
        }

        // Parse credentials
        var username = settings.GetValueOrDefault("credentials.username");
        var password = settings.GetValueOrDefault("credentials.password");

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            _credentials = new NetworkCredential(username, password);
        }
    }    public async Task<Dictionary<string, object>> Execute(string function, Dictionary<string, object> parameters)
    {
        return function.ToLowerInvariant() switch
        {
            "base64file" => await StoreFile(parameters),
            "postfile" => await HttpPostFile(parameters),
            "httppostfile" => await HttpPostFile(parameters),
            _ => throw new NotSupportedException($"Function '{function}' is not supported by {Name} plugin")
        };
    }

    private async Task<Dictionary<string, object>> StoreFile(Dictionary<string, object> parameters)
    {
        try
        {
            if (!parameters.TryGetValue("file", out var fileObj) || fileObj is not string fileBase64)
            {
                throw new ArgumentException("Parameter 'file' (base64 encoded) is required");
            }

            if (!parameters.TryGetValue("fileName", out var fileNameObj) || fileNameObj is not string fileName)
            {
                throw new ArgumentException("Parameter 'fileName' is required");
            }

            if (!parameters.TryGetValue("destinationKey", out var destKeyObj) || destKeyObj is not string destinationKey)
            {
                throw new ArgumentException("Parameter 'destinationKey' is required");
            }

            if (!_destinations.TryGetValue(destinationKey, out var destinationPath))
            {
                throw new ArgumentException($"Destination '{destinationKey}' not found in configuration");
            }

            // Decode base64 file data
            var fileBytes = Convert.FromBase64String(fileBase64);

            // Combine destination path with filename
            // Validate fileName is just a filename, not a path
            if (Path.IsPathRooted(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException("Parameter 'fileName' must be a simple filename without path separators");
            }
            var fullPath = Path.Combine(destinationPath, fileName);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write file
            await File.WriteAllBytesAsync(fullPath, fileBytes);

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["filePath"] = fullPath,
                ["fileSize"] = fileBytes.Length,
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
    private async Task<Dictionary<string, object>> HttpPostFile(Dictionary<string, object> parameters)
    {
        try
        {
            // Check if a file stream was provided
            if (!parameters.TryGetValue("__fileStream", out var fileStreamObj) || fileStreamObj is not Stream fileStream)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "No file uploaded. Use multipart/form-data to upload a file.",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }

            // Get file metadata
            var fileName = parameters.TryGetValue("__fileName", out var fnObj) ? fnObj.ToString() : "unknown";
            var fileSize = parameters.TryGetValue("__fileSize", out var fsObj) ? (long)fsObj : 0;
            var contentType = parameters.TryGetValue("__contentType", out var ctObj) ? ctObj.ToString() : "application/octet-stream";

            // Get optional target filename from form parameters
            var targetFileName = parameters.TryGetValue("targetFileName", out var tfnObj) ? tfnObj.ToString() : fileName;
            
            // Get optional subdirectory from form parameters
            var subDirectory = parameters.TryGetValue("subDirectory", out var sdObj) ? sdObj.ToString() : "";

            // Get storage path from settings or use default
            var storagePath = _destinations.GetValueOrDefault("StoragePath", Path.Combine(Path.GetTempPath(), "TokenRelay", "FileStorage"));
            
            // Determine storage path
            var storageDir = string.IsNullOrEmpty(subDirectory) ? storagePath : Path.Combine(storagePath, subDirectory);
            if (!Directory.Exists(storageDir))
            {
                Directory.CreateDirectory(storageDir);
            }

            // Generate unique filename to avoid conflicts
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(targetFileName ?? "file");
            var extension = Path.GetExtension(targetFileName ?? "");
            var uniqueFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";
            var fullPath = Path.Combine(storageDir, uniqueFileName);

            // Copy file stream to storage
            using (var destinationStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await fileStream.CopyToAsync(destinationStream);
            }

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["message"] = "File uploaded successfully",
                ["originalFileName"] = fileName ?? "unknown",
                ["storedFileName"] = uniqueFileName,
                ["filePath"] = fullPath,
                ["fileSize"] = fileSize,
                ["contentType"] = contentType ?? "application/octet-stream",
                ["subDirectory"] = subDirectory ?? "",
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
