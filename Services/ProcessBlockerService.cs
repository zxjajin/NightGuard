using System.Collections.Concurrent;
using System.Diagnostics;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class ProcessBlockerService : IDisposable
{
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
        "TextInputHost",
        "SecurityHealthSystray",
        "SystemSettings",
        "Taskmgr"
    ];

    private readonly JsonLogService _logService;
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

    public Func<string, AppAccessChoice>? AccessRequested { get; set; }

    public ProcessBlockerService(JsonLogService logService)
    {
        _logService = logService;
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

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _temporaryAllowedUntil.Clear();
        _promptCooldownUntil.Clear();
        _promptInProgress.Clear();
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
                    if (!ShouldBlock(process, processName, now))
                    {
                        continue;
                    }

                    var id = process.Id;
                    process.Kill(entireProcessTree: true);
                    _logService.RecordBlockedProcess(_nightKey, processName, id);
                    AskForTemporaryAccess(processName, now);
                }
                catch
                {
                    // Some system processes deny inspection or termination; ignoring keeps the guard stable.
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

    private bool ShouldBlock(Process process, string processName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (process.Id == _currentProcessId || processName.Equals(_currentProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_temporaryAllowedUntil.TryGetValue(processName, out var allowedUntil))
        {
            if (allowedUntil > now)
            {
                return false;
            }

            _temporaryAllowedUntil.TryRemove(processName, out _);
        }

        if (process.Id <= 4)
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

        return _config.BlockAllAppsDuringRestriction || blocked.Contains(processName);
    }

    private void AskForTemporaryAccess(string processName, DateTimeOffset now)
    {
        if (AccessRequested is null)
        {
            return;
        }

        if (!_promptInProgress.TryAdd(processName, 0))
        {
            return;
        }

        if (_promptCooldownUntil.TryGetValue(processName, out var cooldownUntil) && cooldownUntil > now)
        {
            _promptInProgress.TryRemove(processName, out _);
            return;
        }

        _promptCooldownUntil[processName] = DateTimeOffset.Now.AddSeconds(30);
        if (!_promptGate.Wait(0))
        {
            _promptInProgress.TryRemove(processName, out _);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var choice = AccessRequested.Invoke(processName);
                var allowedUntil = choice switch
                {
                    AppAccessChoice.AllowOneMinute => DateTimeOffset.Now.AddMinutes(1),
                    AppAccessChoice.AllowFifteenMinutes => DateTimeOffset.Now.AddMinutes(15),
                    AppAccessChoice.AllowTonight => _restrictionEndsAt,
                    _ => (DateTimeOffset?)null
                };

                if (allowedUntil is not null)
                {
                    _temporaryAllowedUntil[processName] = allowedUntil.Value;
                    _logService.RecordSystemMessage(_nightKey, $"temporary app access granted: {processName} until {allowedUntil.Value:yyyy-MM-dd HH:mm:ss zzz}");
                }
            }
            finally
            {
                _promptInProgress.TryRemove(processName, out _);
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

    public void Dispose()
    {
        Stop();
        _promptGate.Dispose();
    }
}
