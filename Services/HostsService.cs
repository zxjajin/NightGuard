using System.IO;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class HostsService
{
    private const string BeginMarker = "# NightGuard BEGIN";
    private const string EndMarker = "# NightGuard END";
    private readonly string _hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    private readonly string _backupDirectory;
    private readonly JsonLogService _logService;
    private readonly GuardActionRecordService _recordService;

    public HostsService(string dataDirectory, JsonLogService logService, GuardActionRecordService recordService)
    {
        _backupDirectory = Path.Combine(dataDirectory, "hosts-backups");
        _logService = logService;
        _recordService = recordService;
    }

    public void Apply(AppConfig config, string nightKey)
    {
        ApplyDomains(config.BlockedDomains, config.AlwaysAllowedDomains, nightKey, "限制模式启用网站规则");
    }

    public void ApplyDomains(IEnumerable<string> domainsToApply, string nightKey, string note)
    {
        ApplyDomains(domainsToApply, [], nightKey, note);
    }

    public void ApplyDomains(IEnumerable<string> domainsToApply, IEnumerable<string> alwaysAllowedDomains, string nightKey, string note)
    {
        Directory.CreateDirectory(_backupDirectory);
        Backup(nightKey);

        var current = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : "";
        current = RemoveNightGuardBlock(current);

        var allowSet = alwaysAllowedDomains
            .Select(domain => domain.Trim())
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var domains = domainsToApply
            .Select(domain => domain.Trim())
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Where(domain => !allowSet.Contains(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (domains.Count == 0)
        {
            File.WriteAllText(_hostsPath, current);
            _recordService.Add("hosts 写入", "hosts", "清空 NightGuard 区块", "成功", "网站黑名单为空或被白名单排除");
            return;
        }

        var block = string.Join(Environment.NewLine, domains.Select(domain => $"127.0.0.1 {domain}"));
        var newHosts = $"{current.TrimEnd()}{Environment.NewLine}{BeginMarker}{Environment.NewLine}{block}{Environment.NewLine}{EndMarker}{Environment.NewLine}";
        File.WriteAllText(_hostsPath, newHosts);
        foreach (var domain in domains)
        {
            _recordService.Add("hosts 写入", domain, "添加规则", "成功", note);
        }
    }

    public void Restore(string nightKey)
    {
        if (!File.Exists(_hostsPath))
        {
            return;
        }

        var current = File.ReadAllText(_hostsPath);
        var restored = RemoveNightGuardBlock(current);
        File.WriteAllText(_hostsPath, restored.TrimEnd() + Environment.NewLine);
        _logService.RecordSystemMessage(nightKey, "hosts restored");
        _recordService.Add("hosts 恢复", "hosts", "移除规则", "成功", "NightGuard hosts 区块已移除");
    }

    private void Backup(string nightKey)
    {
        if (!File.Exists(_hostsPath))
        {
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(_backupDirectory, $"hosts-{nightKey}-{stamp}.bak");
        File.Copy(_hostsPath, backupPath, overwrite: false);
    }

    private static string RemoveNightGuardBlock(string text)
    {
        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return text;
        }

        end += EndMarker.Length;
        while (end < text.Length && (text[end] == '\r' || text[end] == '\n'))
        {
            end++;
        }

        return text.Remove(begin, end - begin);
    }
}
