using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class ProcessBlockerService : IDisposable
{
    private const int ShowWindowMinimize = 6;

    private static readonly string[] BuiltInAllowedProcesses =
    [
        "Idle",
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "lsaiso",
        "svchost",
        "fontdrvhost",
        "WUDFHost",
        "WmiPrvSE",
        "audiodg",
        "spoolsv",
        "dllhost",
        "SearchIndexer",
        "SecurityHealthService",
        "MsMpEng",
        "NisSrv",
        "NightGuard",
        "dotnet",
        "explorer",
        "dwm",
        "sihost",
        "ctfmon",
        "conhost",
        "taskhostw",
        "RuntimeBroker",
        "ApplicationFrameHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchProtocolHost",
        "SearchFilterHost",
        "smartscreen",
        "rundll32",
        "backgroundTaskHost",
        "CompPkgSrv",
        "unsecapp",
        "AggregatorHost",
        "dasHost",
        "WmiApSrv",
        "wlanext",
        "RtkAudUService64",
        "NVDisplay.Container",
        "nvcontainer",
        "nvsphelper64",
        "NVIDIA Overlay",
        "NVIDIA Share",
        "NVIDIA Web Helper",
        "NVIDIA App",
        "Clash Verge",
        "clash",
        "clash-meta",
        "clash-verge-service",
        "FlClash",
        "FlClashHelperService",
        "Clash for Windows",
        "Clash Core Service",
        "node_repl",
        "extension-host",
        "crashpad_handler",
        "node",
        "git",
        "git-remote-https",
        "rg",
        "fd",
        "PixPin",
        "PixPinCapture",
        "PixPinService",
        "aw-qt",
        "aw-server",
        "aw-watcher-afk",
        "aw-watcher-window",
        "Weixin",
        "WeChat",
        "WeChatAppEx",
        "WeChatBrowser",
        "WeChatUtility",
        "WeChatPlayer",
        "YoudaoDict",
        "YoudaoDictHelper",
        "YoudaoEDIT",
        "YoudaoWSH",
        "YoudaoOcr",
        "YoudaoDesktopDict"
    ];

    private readonly JsonLogService _logService;
    private readonly GuardActionRecordService _recordService;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _temporaryAllowedUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _promptCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _promptInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _promptGate = new(1, 1);
    private readonly int _currentProcessId = Environment.ProcessId;
    private readonly string _currentProcessName = NormalizeProcessName(Process.GetCurrentProcess().ProcessName);
    private System.Threading.Timer? _timer;
    private AppConfig _config = new();
    private string _nightKey = DateTime.Today.ToString("yyyy-MM-dd");
    private DateTimeOffset _restrictionEndsAt = DateTimeOffset.MaxValue;
    private int _scanRunning;

    public Func<ExplorerAccessRequest, AppAccessChoice>? AccessRequested { get; set; }

    public ProcessBlockerService(JsonLogService logService, GuardActionRecordService recordService)
    {
        _logService = logService;
        _recordService = recordService;
    }

    public void Start(AppConfig config, string nightKey, DateTimeOffset restrictionEndsAt)
    {
        _config = config;
        _nightKey = nightKey;
        _restrictionEndsAt = restrictionEndsAt;
        _timer ??= new System.Threading.Timer(_ => Scan(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void UpdateConfig(AppConfig config, string nightKey, DateTimeOffset restrictionEndsAt)
    {
        _config = config;
        _nightKey = nightKey;
        _restrictionEndsAt = restrictionEndsAt;
    }

    public IReadOnlyList<string> GetRunningTargetProcesses(IEnumerable<string> processNames)
    {
        var targets = processNames
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            foreach (var process in Process.GetProcessesByName(target))
            {
                try
                {
                    if (process.Id == _currentProcessId)
                    {
                        continue;
                    }

                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    results.Add($"{NormalizeProcessName(process.ProcessName)}.exe");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return results.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool IsTemporarilyAllowed(string processName, DateTimeOffset now)
    {
        var normalized = NormalizeProcessName(processName);
        if (_temporaryAllowedUntil.TryGetValue(normalized, out var allowedUntil))
        {
            if (allowedUntil > now)
            {
                return true;
            }

            _temporaryAllowedUntil.TryRemove(normalized, out _);
        }

        return false;
    }

    public void GrantTemporaryAccess(string processName, TimeSpan duration, string reason)
    {
        var normalized = NormalizeProcessName(processName);
        var allowedUntil = DateTimeOffset.Now.Add(duration);
        _temporaryAllowedUntil[normalized] = allowedUntil;
        _logService.RecordSystemMessage(_nightKey, $"temporary app access granted: {normalized} until {allowedUntil:yyyy-MM-dd HH:mm:ss zzz}; {reason}");
    }

    public int MinimizeProcessWindows(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        var minimized = 0;
        foreach (var process in Process.GetProcessesByName(normalized))
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (ShowWindowAsync(process.MainWindowHandle, ShowWindowMinimize))
                {
                    minimized++;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return minimized;
    }

    public AppAccessChoice? RequestAccess(ExplorerAccessRequest request, string promptKey, DateTimeOffset now)
    {
        if (AccessRequested is null)
        {
            return null;
        }

        if (!_promptInProgress.TryAdd(promptKey, 0))
        {
            return null;
        }

        try
        {
            if (_promptCooldownUntil.TryGetValue(promptKey, out var cooldownUntil) && cooldownUntil > now)
            {
                return null;
            }

            _promptCooldownUntil[promptKey] = DateTimeOffset.Now.AddSeconds(30);
            return AccessRequested.Invoke(request);
        }
        finally
        {
            _promptInProgress.TryRemove(promptKey, out _);
        }
    }

    public void SuppressPrompt(string promptKey, TimeSpan duration)
    {
        _promptCooldownUntil[promptKey] = DateTimeOffset.Now.Add(duration);
    }

    public void KillProcessByName(string processName, string type, string action, string note)
    {
        var normalized = NormalizeProcessName(processName);
        foreach (var process in Process.GetProcessesByName(normalized))
        {
            try
            {
                var id = process.Id;
                process.Kill(entireProcessTree: true);
                _logService.RecordBlockedProcess(_nightKey, normalized, id);
                _recordService.Add(type, $"{normalized}.exe", action, "成功", note);
            }
            catch (Exception ex)
            {
                _recordService.Add(type, $"{normalized}.exe", action, "失败", ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _temporaryAllowedUntil.Clear();
        _promptCooldownUntil.Clear();
        _promptInProgress.Clear();
    }

    public int RunSafeTest(TestModeOptions options)
    {
        var targetName = NormalizeProcessName(options.TargetProcessName);
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(targetName))
        {
            try
            {
                var id = process.Id;
                process.Kill(entireProcessTree: true);
                killed++;
                _logService.RecordBlockedProcess(_nightKey, targetName, id);
                _recordService.Add("测试模式", $"{targetName}.exe", "结束测试进程", "成功", "安全测试模式只处理指定测试程序");
            }
            catch (Exception ex)
            {
                _recordService.Add("测试模式", $"{targetName}.exe", "结束测试进程", "失败", ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        if (killed == 0)
        {
            _recordService.Add("测试模式", $"{targetName}.exe", "检测测试进程", "未检测到", "未处理任何真实应用或 hosts");
        }

        return killed;
    }

    private void Scan()
    {
        if (Interlocked.Exchange(ref _scanRunning, 1) == 1)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var processName = NormalizeProcessName(process.ProcessName);
                    var hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    if (!ShouldBlock(process, processName, now, hasWindow))
                    {
                        continue;
                    }

                    var id = process.Id;
                    process.Kill(entireProcessTree: true);
                    _logService.RecordBlockedProcess(_nightKey, processName, id);
                    _recordService.Add("应用拦截", $"{processName}.exe", "结束进程", "成功", hasWindow ? "前台应用命中限制规则" : "黑名单进程命中限制规则");
                    if (hasWindow)
                    {
                        AskForTemporaryAccess(processName, now);
                    }
                }
                catch (Exception ex)
                {
                    _recordService.Add("应用拦截", "未知进程", "结束进程", "失败", ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _scanRunning, 0);
        }
    }

    private bool ShouldBlock(Process process, string processName, DateTimeOffset now, bool hasWindow)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (process.Id == _currentProcessId || processName.Equals(_currentProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsTemporarilyAllowed(processName, now))
        {
            return false;
        }

        if (process.Id <= 4 || process.SessionId == 0)
        {
            return false;
        }

        if (BuiltInAllowedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var alwaysAllowed = _config.AlwaysAllowedProcesses
            .Select(NormalizeProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (alwaysAllowed.Contains(processName))
        {
            return false;
        }

        var blocked = _config.BlockedProcesses
            .Select(NormalizeProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blocked.Contains(processName))
        {
            return true;
        }

        return _config.BlockAllAppsDuringRestriction && hasWindow;
    }

    private void AskForTemporaryAccess(string processName, DateTimeOffset now)
    {
        if (!_promptGate.Wait(0))
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var choice = RequestAccess(new ExplorerAccessRequest
                {
                    TargetName = $"{processName}.exe",
                    Mode = ExplorerAccessMode.LimitedWrapUp
                }, processName, now);

                TimeSpan? duration = choice switch
                {
                    AppAccessChoice.AllowOneMinute => TimeSpan.FromMinutes(1),
                    AppAccessChoice.AllowTenMinutes => TimeSpan.FromMinutes(10),
                    AppAccessChoice.AllowFifteenMinutes => TimeSpan.FromMinutes(15),
                    AppAccessChoice.AllowTonight => _restrictionEndsAt - DateTimeOffset.Now,
                    _ => null
                };

                if (duration is not null && duration.Value > TimeSpan.Zero)
                {
                    GrantTemporaryAccess(processName, duration.Value, "general restriction flow");
                }
            }
            finally
            {
                _promptGate.Release();
            }
        });
    }

    private static string NormalizeProcessName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    public void Dispose()
    {
        Stop();
        _promptGate.Dispose();
    }
}
