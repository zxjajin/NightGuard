using System.IO;
using System.Text.Json;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class ConfigService
{
    private static readonly string[] AiAllowedDomains =
    [
        "chatgpt.com",
        "chat.openai.com",
        "claude.ai",
        "gemini.google.com",
        "perplexity.ai"
    ];

    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Data");
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public AppConfig Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(ConfigPath))
        {
            var initialConfig = NormalizeForCurrentPolicy(new AppConfig());
            Save(initialConfig);
            return initialConfig;
        }

        var json = File.ReadAllText(ConfigPath);
        var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        config = NormalizeForCurrentPolicy(config);
        Save(config);
        return config;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DataDirectory);
        var normalized = NormalizeForCurrentPolicy(config);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, _jsonOptions));
    }

    private static AppConfig NormalizeForCurrentPolicy(AppConfig config)
    {
        config.AlwaysAllowedProcesses ??= [];
        config.AlwaysAllowedDomains ??= [];
        config.BlockedProcesses ??= [];
        config.BlockedDomains ??= [];
        config.NightFocusProcesses ??= [];
        config.NightFocusDomains ??= [];

        foreach (var domain in AiAllowedDomains)
        {
            if (!config.AlwaysAllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                config.AlwaysAllowedDomains.Add(domain);
            }
        }

        config.AlwaysAllowedDomains = NormalizeList(config.AlwaysAllowedDomains.Select(item => item.ToLowerInvariant()));
        config.NightFocusDomains = [];
        config.AlwaysAllowedProcesses = NormalizeList(config.AlwaysAllowedProcesses);
        config.BlockedProcesses = NormalizeList(config.BlockedProcesses);
        config.BlockedDomains = NormalizeList(config.BlockedDomains.Select(item => item.ToLowerInvariant()));
        config.NightFocusProcesses = NormalizeList(config.NightFocusProcesses);
        return config;
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
