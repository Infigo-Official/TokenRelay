using System.Reflection;
using TokenRelay.Models;
using TokenRelay.Plugins;

namespace TokenRelay.Services;

public interface IPluginService
{
    Task<Dictionary<string, object>> ExecutePluginFunctionAsync(string pluginName, string functionName, Dictionary<string, object> parameters);
    List<PluginInfo> GetLoadedPlugins();
    void LoadPluginsAsync();
}

public class PluginService : IPluginService
{
    private readonly ILogger<PluginService> _logger;
    private readonly IConfigurationService _configService;
    private readonly Dictionary<string, ITokenRelayPlugin> _loadedPlugins = new();

    public PluginService(ILogger<PluginService> logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public async Task<Dictionary<string, object>> ExecutePluginFunctionAsync(
        string pluginName, 
        string functionName, 
        Dictionary<string, object> parameters)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out var plugin))
        {
            throw new ArgumentException($"Plugin '{pluginName}' not found or not loaded");
        }

        try
        {
            _logger.LogInformation("Executing function {FunctionName} on plugin {PluginName}", 
                functionName, pluginName);

            var result = await plugin.Execute(functionName, parameters);
            
            _logger.LogInformation("Successfully executed function {FunctionName} on plugin {PluginName}", 
                functionName, pluginName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing function {FunctionName} on plugin {PluginName}", 
                functionName, pluginName);
            
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["data"] = new Dictionary<string, object>()
            };
        }
    }

    public List<PluginInfo> GetLoadedPlugins()
    {
        return _loadedPlugins.Values.Select(plugin => new PluginInfo
        {
            Name = plugin.Name,
            Version = plugin.Version,
            Status = "loaded"
        }).ToList();
    }

    public void LoadPluginsAsync()
    {
        try
        {
            _logger.LogInformation("PluginService: Starting plugin loading process");

            // For now, we'll load built-in plugins
            // In the future, this could scan directories for plugin assemblies
            
            // Load file storage plugin if configured
            var config = _configService.GetConfiguration();
            _logger.LogDebug("PluginService: Checking configuration for {PluginCount} plugin entries", 
                config.Plugins.Count);
            
            if (config.Plugins.TryGetValue("fileStorage", out var fileStorageConfig))
            {
                if (fileStorageConfig.Enabled)
                {
                    _logger.LogDebug("PluginService: Loading file storage plugin");
                    var fileStoragePlugin = new FileStoragePlugin();
                    fileStoragePlugin.Configure(fileStorageConfig.Settings);
                    _loadedPlugins[fileStoragePlugin.Name] = fileStoragePlugin;
                    
                    _logger.LogInformation("PluginService: Loaded plugin '{PluginName}' v{Version}", 
                        fileStoragePlugin.Name, fileStoragePlugin.Version);
                }
                else
                {
                    _logger.LogDebug("PluginService: File storage plugin is disabled in configuration");
                }
            }
            else
            {
                _logger.LogDebug("PluginService: No file storage plugin configuration found");
            }

            _logger.LogInformation("PluginService: Plugin loading completed - {PluginCount} plugins loaded", 
                _loadedPlugins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PluginService: Error loading plugins");
            throw;
        }
    }
}
