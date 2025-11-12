namespace TokenRelay.Plugins;

public interface ITokenRelayPlugin
{
    string Name { get; }
    string Version { get; }
    Task<Dictionary<string, object>> Execute(string function, Dictionary<string, object> parameters);
    void Configure(Dictionary<string, string> settings);
}

public class PluginResponse
{
    public bool Success { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public string? Error { get; set; }
}
