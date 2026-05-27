using System.IO;
using System.Text.Json;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class ConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Data");
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public AppConfig Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(ConfigPath))
        {
            var config = new AppConfig();
            Save(config);
            return config;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, _jsonOptions));
    }
}
