using System.IO;
using System.Text.Json;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class ConfigImportExportService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void Export(AppConfig config, string path)
    {
        var package = new ConfigExportPackage
        {
            Version = 1,
            ExportedAt = DateTimeOffset.Now,
            AppRules = [.. config.BlockedProcesses],
            HostRules = [.. config.BlockedDomains],
            Settings = config
        };

        File.WriteAllText(path, JsonSerializer.Serialize(package, _jsonOptions));
    }

    public AppConfig Import(string path)
    {
        var json = File.ReadAllText(path);
        var package = JsonSerializer.Deserialize<ConfigExportPackage>(json, _jsonOptions);
        var config = package?.Settings;

        // Backward-compatible fallback: allow importing a raw AppConfig JSON.
        config ??= JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException("配置文件格式不正确。");
        }

        Validate(config);
        Normalize(config);
        return config;
    }

    private static void Validate(AppConfig config)
    {
        if (!TimeOnly.TryParse(config.RestrictionStart, out _))
        {
            throw new InvalidOperationException("限制开始时间格式不正确，应为 HH:mm。");
        }

        if (!TimeOnly.TryParse(config.RestrictionEnd, out _))
        {
            throw new InvalidOperationException("限制结束时间格式不正确，应为 HH:mm。");
        }

        if (config.UnlockDelayMinutes < 0 || config.TemporaryAllowanceMinutes < 0 || config.MaxUnlocksPerNight < 0)
        {
            throw new InvalidOperationException("解锁相关分钟数和次数不能为负数。");
        }
    }

    private static void Normalize(AppConfig config)
    {
        config.AlwaysAllowedProcesses ??= [];
        config.AlwaysAllowedDomains ??= [];
        config.BlockedProcesses ??= [];
        config.BlockedDomains ??= [];
        config.NightFocusProcesses ??= [];
        config.NightFocusDomains ??= [];
        config.AlwaysAllowedProcesses = NormalizeList(config.AlwaysAllowedProcesses);
        config.AlwaysAllowedDomains = NormalizeList(config.AlwaysAllowedDomains.Select(domain => domain.ToLowerInvariant()));
        config.BlockedProcesses = NormalizeList(config.BlockedProcesses);
        config.BlockedDomains = NormalizeList(config.BlockedDomains.Select(domain => domain.ToLowerInvariant()));
        config.NightFocusProcesses = NormalizeList(config.NightFocusProcesses);
        config.NightFocusDomains = NormalizeList(config.NightFocusDomains.Select(domain => domain.ToLowerInvariant()));
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
