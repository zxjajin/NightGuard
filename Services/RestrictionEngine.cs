using System.ComponentModel;
using System.Runtime.CompilerServices;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class RestrictionEngine : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeOnly NightFocusReminderStart = new(23, 0);
    private static readonly TimeOnly NightFocusLimitedStart = new(23, 30);
    private static readonly TimeOnly NightFocusEnd = new(7, 0);
    private static readonly TimeSpan NightFocusRemindLaterDuration = TimeSpan.FromMinutes(15);

    private readonly ConfigService _configService;
    private readonly JsonLogService _logService;
    private readonly HostsService _hostsService;
    private readonly ProcessBlockerService _processBlocker;
    private readonly GuardActionRecordService _recordService;
    private readonly System.Threading.Timer _timer;
    private bool _restrictionApplied;
    private bool _restrictionStartLogged;
    private DateTimeOffset? _unlockRequestedAt;
    private DateTimeOffset? _temporaryAllowedUntil;
    private string _nightKey = DateTime.Today.ToString("yyyy-MM-dd");
    private string _statusNote = "白天不限制 Codex / GPT / Cursor / AI 网站。";
    private string _lastHostsSignature = "";
    private string _lastHostsAttemptSignature = "";
    private bool _hostsApplied;
    private bool _hasSyncedHosts;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RestrictionStarted;

    public AppConfig Config { get; private set; }
    public GuardState State { get; private set; } = GuardState.NotRestricted;
    public string StatusText => State switch
    {
        GuardState.Restricted => "限制中",
        GuardState.WaitingForTemporaryUnlock => "等待临时解锁",
        GuardState.TemporaryAllowed => "临时放行中",
        _ => "未限制"
    };
    public string StatusNote => _statusNote;
    public TimeSpan RemainingRestrictionTime { get; private set; }
    public TimeSpan UnlockCountdown { get; private set; }
    public string RemainingRestrictionDisplay => $"今天剩余限制时间：{FormatTimeSpan(RemainingRestrictionTime)}";
    public string UnlockCountdownDisplay => $"倒计时：{FormatTimeSpan(UnlockCountdown)}";
    public string TonightUnlockCountDisplay => $"今晚已临时解锁次数：{TonightUnlockCount}";
    public string RuleSummaryDisplay => $"应用白名单：{Config.AlwaysAllowedProcesses.Count} 个；网站黑名单：{Config.BlockedDomains.Count} 个；hosts 限制：{(Config.EnableHostsBlocking ? "开启" : "关闭")}";
    public int TonightUnlockCount { get; private set; }
    public bool CanEditRules => State is GuardState.NotRestricted;
    public bool CanRequestTemporaryUnlock => State is GuardState.Restricted && TonightUnlockCount < Config.MaxUnlocksPerNight;

    public RestrictionEngine(ConfigService configService, JsonLogService logService, HostsService hostsService, ProcessBlockerService processBlocker, GuardActionRecordService recordService)
    {
        _configService = configService;
        _logService = logService;
        _hostsService = hostsService;
        _processBlocker = processBlocker;
        _recordService = recordService;
        Config = _configService.Load();
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void ReloadConfig()
    {
        Config = _configService.Load();
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(RuleSummaryDisplay));
        Tick();
    }

    public void RequestTemporaryUnlock()
    {
        if (!CanRequestTemporaryUnlock)
        {
            return;
        }

        _unlockRequestedAt = DateTimeOffset.Now;
        SetState(GuardState.WaitingForTemporaryUnlock);
        OnAllStatusChanged();
    }

    public void RestoreHostsOnExit()
    {
        try
        {
            _hostsService.Restore(GetNightFocusNightKey(DateTimeOffset.Now));
            _hostsApplied = false;
            _lastHostsSignature = "";
            _lastHostsAttemptSignature = "__restore__";
            _hasSyncedHosts = true;
        }
        catch (Exception ex)
        {
            SetStatusNote($"hosts 恢复失败：{ex.Message}");
            _recordService.Add("hosts 恢复失败", "hosts", "退出恢复", "失败", ex.Message);
        }
    }

    public void RestoreHostsNow()
    {
        _hostsService.Restore(GetNightFocusNightKey(DateTimeOffset.Now));
        _logService.RecordSystemMessage(GetNightFocusNightKey(DateTimeOffset.Now), "hosts restored manually");
        _hostsApplied = false;
        _lastHostsSignature = "";
        _lastHostsAttemptSignature = "__restore__";
        _hasSyncedHosts = true;
        SetStatusNote("hosts 已手动恢复。");
    }

    public void StartTestMode(TimeSpan duration)
    {
        var options = new TestModeOptions();
        var killed = _processBlocker.RunSafeTest(options);
        _recordService.Add(
            "测试模式",
            options.TargetProcessName,
            "安全测试",
            killed > 0 ? "成功" : "未检测到",
            killed > 0 ? $"已处理测试进程 {killed} 个；未修改 hosts。" : "未检测到测试程序；未修改 hosts，未处理其他应用。");
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        _nightKey = GetNightKey(now);
        HandleGeneralRestriction(now);
        HandleNightFocus(now);
        UpdateHosts(now);
    }

    private void HandleGeneralRestriction(DateTimeOffset now)
    {
        var window = GetCurrentRestrictionWindow(now);
        var inRestrictionWindow = window is not null;
        TonightUnlockCount = _logService.GetTemporaryUnlockCount(_nightKey);
        RemainingRestrictionTime = inRestrictionWindow ? window!.Value.End - now : TimeSpan.Zero;

        if (_restrictionApplied && window is not null)
        {
            _processBlocker.UpdateConfig(Config, _nightKey, window.Value.End);
        }

        if (!inRestrictionWindow)
        {
            ExitRestrictionMode();
            _unlockRequestedAt = null;
            _temporaryAllowedUntil = null;
            _restrictionStartLogged = false;
            SetState(GuardState.NotRestricted);
            OnAllStatusChanged();
            return;
        }

        if (!_restrictionStartLogged)
        {
            _logService.RecordRestrictionStarted(_nightKey, now);
            _recordService.Add("限制状态", _nightKey, "限制开始", "成功", "按计划时间段触发");
            _restrictionStartLogged = true;
            RestrictionStarted?.Invoke(this, EventArgs.Empty);
        }

        if (State == GuardState.WaitingForTemporaryUnlock && _unlockRequestedAt is not null)
        {
            var unlockAt = _unlockRequestedAt.Value.AddMinutes(Config.UnlockDelayMinutes);
            UnlockCountdown = unlockAt - now;
            if (UnlockCountdown <= TimeSpan.Zero)
            {
                _temporaryAllowedUntil = now.AddMinutes(Config.TemporaryAllowanceMinutes);
                _logService.RecordTemporaryUnlock(_nightKey, _unlockRequestedAt.Value, now, _temporaryAllowedUntil.Value);
                TonightUnlockCount = _logService.GetTemporaryUnlockCount(_nightKey);
                ExitRestrictionMode();
                SetState(GuardState.TemporaryAllowed);
            }

            OnAllStatusChanged();
            return;
        }

        if (State == GuardState.TemporaryAllowed && _temporaryAllowedUntil is not null)
        {
            RemainingRestrictionTime = window!.Value.End - now;
            if (now < _temporaryAllowedUntil)
            {
                UnlockCountdown = _temporaryAllowedUntil.Value - now;
                ExitRestrictionMode();
                OnAllStatusChanged();
                return;
            }

            _temporaryAllowedUntil = null;
            _unlockRequestedAt = null;
        }

        EnterRestrictionMode(window!.Value.End);
        UnlockCountdown = TimeSpan.Zero;
        SetState(GuardState.Restricted);
        OnAllStatusChanged();
    }

    private void HandleNightFocus(DateTimeOffset now)
    {
        if (State is GuardState.WaitingForTemporaryUnlock or GuardState.TemporaryAllowed)
        {
            SetStatusNote(State == GuardState.WaitingForTemporaryUnlock
                ? "全局临时解锁申请中，夜间探索限制暂不追加处理。"
                : "全局临时解锁生效中，夜间探索限制暂时放行。");
            return;
        }

        var focusWindow = GetNightFocusWindow(now);
        var focusNightKey = GetNightFocusNightKey(now);

        switch (focusWindow)
        {
            case NightFocusWindow.Daytime:
                SetStatusNote("白天不限制 Codex / GPT / Cursor / AI 网站。");
                return;
            case NightFocusWindow.Reminder:
                HandleNightFocusReminder(now, focusNightKey);
                return;
            case NightFocusWindow.Limited:
                HandleNightFocusLimited(now, focusNightKey);
                return;
        }
    }

    private void HandleNightFocusReminder(DateTimeOffset now, string nightKey)
    {
        SetStatusNote("23:00 - 23:30 仅提醒，不拦截夜间探索型工具。");

        foreach (var target in _processBlocker.GetRunningTargetProcesses(Config.NightFocusProcesses))
        {
            if (_logService.HasNightFocusReminder(nightKey, target))
            {
                continue;
            }

            _processBlocker.RequestAccess(new ExplorerAccessRequest
            {
                TargetName = target,
                Mode = ExplorerAccessMode.ReminderOnly
            }, $"reminder:{target}", now);

            _logService.RecordNightFocusReminder(nightKey, target);
            _recordService.Add("夜间收尾提醒", target, "提醒一次", "成功", "23:00 - 23:30 只提醒，不拦截");
        }
    }

    private void HandleNightFocusLimited(DateTimeOffset now, string nightKey)
    {
        SetStatusNote("23:30 - 07:00 夜间探索型工具进入收束提醒：可本晚不再限制、记录到明天并最小化，或 15 分钟后再提醒。");

        foreach (var target in _processBlocker.GetRunningTargetProcesses(Config.NightFocusProcesses))
        {
            if (_processBlocker.IsTemporarilyAllowed(target, now))
            {
                continue;
            }

            var choice = _processBlocker.RequestAccess(new ExplorerAccessRequest
            {
                TargetName = target,
                Mode = ExplorerAccessMode.LimitedWrapUp
            }, $"limited:{target}", now);

            switch (choice)
            {
                case AppAccessChoice.AllowTonight:
                    var untilNightFocusEnds = GetNightFocusEndsAt(now) - now;
                    _processBlocker.GrantTemporaryAccess(target, untilNightFocusEnds, "night focus unlimited tonight");
                    _recordService.Add("本晚不再限制", target, "允许本晚使用", "成功", "夜间探索型工具本晚不再弹出收束提醒");
                    break;
                case AppAccessChoice.RecordForTomorrow:
                    _processBlocker.SuppressPrompt($"limited:{target}", NightFocusRemindLaterDuration);
                    var minimized = _processBlocker.MinimizeProcessWindows(target);
                    _recordService.Add("记录到明天", target, "延后处理并最小化", "成功", minimized > 0 ? "建议明天白天继续；已最小化主窗口" : "建议明天白天继续；未找到可最小化主窗口");
                    break;
                case AppAccessChoice.RemindLater:
                case null:
                    _processBlocker.SuppressPrompt($"limited:{target}", NightFocusRemindLaterDuration);
                    _recordService.Add("稍后再提醒", target, "延后提醒", "成功", "15 分钟后重新提醒");
                    break;
                case AppAccessChoice.Deny:
                    _processBlocker.SuppressPrompt($"limited:{target}", NightFocusRemindLaterDuration);
                    _recordService.Add("关闭提醒", target, "延后提醒", "成功", "15 分钟后重新提醒");
                    break;
            }
        }
    }

    private void EnterRestrictionMode(DateTimeOffset restrictionEndsAt)
    {
        if (_restrictionApplied)
        {
            _processBlocker.UpdateConfig(Config, _nightKey, restrictionEndsAt);
            return;
        }

        _processBlocker.Start(Config, _nightKey, restrictionEndsAt);
        _restrictionApplied = true;
    }

    private void UpdateHosts(DateTimeOffset now)
    {
        var combinedDomains = new List<string>();

        if (GetCurrentRestrictionWindow(now) is not null && Config.EnableHostsBlocking)
        {
            combinedDomains.AddRange(Config.BlockedDomains);
        }

        if (GetNightFocusWindow(now) == NightFocusWindow.Limited)
        {
            combinedDomains.AddRange(Config.NightFocusDomains);
        }

        try
        {
            if (combinedDomains.Count == 0)
            {
                const string restoreSignature = "__restore__";
                if (_lastHostsAttemptSignature == restoreSignature && !_hostsApplied)
                {
                    return;
                }

                if (_hostsApplied)
                {
                    _hostsService.Restore(GetNightFocusNightKey(now));
                    _hostsApplied = false;
                    _lastHostsSignature = "";
                    _lastHostsAttemptSignature = restoreSignature;
                }
                else if (!_hasSyncedHosts)
                {
                    _hostsService.Restore(GetNightFocusNightKey(now));
                    _lastHostsAttemptSignature = restoreSignature;
                }

                _hasSyncedHosts = true;
                return;
            }

            var normalizedDomains = combinedDomains
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var signature = string.Join("|", normalizedDomains);
            if (_hostsApplied && signature == _lastHostsSignature)
            {
                return;
            }

            if (!_hostsApplied && signature == _lastHostsAttemptSignature)
            {
                return;
            }

            _hostsService.ApplyDomains(normalizedDomains, Config.AlwaysAllowedDomains, GetNightFocusNightKey(now), "夜间限制写入网站规则");
            _hostsApplied = true;
            _lastHostsSignature = signature;
            _lastHostsAttemptSignature = signature;
            _hasSyncedHosts = true;
        }
        catch (Exception ex)
        {
            _logService.RecordSystemMessage(GetNightFocusNightKey(now), $"failed to update hosts: {ex.Message}");
            _recordService.Add("hosts 写入失败", "hosts", "写入规则", "失败", ex.Message);
            SetStatusNote($"hosts 写入失败：{ex.Message}");
            _lastHostsAttemptSignature = combinedDomains.Count == 0
                ? "__restore__"
                : string.Join("|", combinedDomains
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Select(domain => domain.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase));
            _hasSyncedHosts = true;
        }
    }

    private void ExitRestrictionMode()
    {
        if (!_restrictionApplied)
        {
            return;
        }

        _processBlocker.Stop();
        _recordService.Add("限制状态", _nightKey, "限制结束", "成功", "退出限制模式");
        _restrictionApplied = false;
    }

    private NightFocusWindow GetNightFocusWindow(DateTimeOffset now)
    {
        var time = TimeOnly.FromDateTime(now.DateTime);
        if (time >= NightFocusReminderStart && time < NightFocusLimitedStart)
        {
            return NightFocusWindow.Reminder;
        }

        if (time >= NightFocusLimitedStart || time < NightFocusEnd)
        {
            return NightFocusWindow.Limited;
        }

        return NightFocusWindow.Daytime;
    }

    private (DateTimeOffset Start, DateTimeOffset End)? GetCurrentRestrictionWindow(DateTimeOffset now)
    {
        var start = ParseTime(Config.RestrictionStart, new TimeOnly(23, 0));
        var end = ParseTime(Config.RestrictionEnd, new TimeOnly(8, 0));
        var todayStart = new DateTimeOffset(now.Date + start.ToTimeSpan(), now.Offset);
        var todayEnd = new DateTimeOffset(now.Date + end.ToTimeSpan(), now.Offset);

        if (start < end)
        {
            return now >= todayStart && now < todayEnd ? (todayStart, todayEnd) : null;
        }

        var endTomorrow = todayEnd.AddDays(1);
        if (now >= todayStart && now < endTomorrow)
        {
            return (todayStart, endTomorrow);
        }

        var startYesterday = todayStart.AddDays(-1);
        if (now >= startYesterday && now < todayEnd)
        {
            return (startYesterday, todayEnd);
        }

        return null;
    }

    private string GetNightKey(DateTimeOffset now)
    {
        var start = ParseTime(Config.RestrictionStart, new TimeOnly(23, 0));
        var end = ParseTime(Config.RestrictionEnd, new TimeOnly(8, 0));
        if (start > end && TimeOnly.FromDateTime(now.DateTime) < end)
        {
            return now.Date.AddDays(-1).ToString("yyyy-MM-dd");
        }

        return now.Date.ToString("yyyy-MM-dd");
    }

    private static string GetNightFocusNightKey(DateTimeOffset now)
    {
        return TimeOnly.FromDateTime(now.DateTime) < NightFocusEnd
            ? now.Date.AddDays(-1).ToString("yyyy-MM-dd")
            : now.Date.ToString("yyyy-MM-dd");
    }

    private static DateTimeOffset GetNightFocusEndsAt(DateTimeOffset now)
    {
        var todayEnd = new DateTimeOffset(now.Date + NightFocusEnd.ToTimeSpan(), now.Offset);
        return TimeOnly.FromDateTime(now.DateTime) < NightFocusEnd
            ? todayEnd
            : todayEnd.AddDays(1);
    }

    private static TimeOnly ParseTime(string value, TimeOnly fallback)
    {
        return TimeOnly.TryParse(value, out var result) ? result : fallback;
    }

    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.ToString(@"hh\:mm\:ss");
    }

    private void SetState(GuardState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanEditRules));
        OnPropertyChanged(nameof(CanRequestTemporaryUnlock));
    }

    private void SetStatusNote(string value)
    {
        if (_statusNote == value)
        {
            return;
        }

        _statusNote = value;
        OnPropertyChanged(nameof(StatusNote));
    }

    private void OnAllStatusChanged()
    {
        OnPropertyChanged(nameof(RemainingRestrictionTime));
        OnPropertyChanged(nameof(UnlockCountdown));
        OnPropertyChanged(nameof(RemainingRestrictionDisplay));
        OnPropertyChanged(nameof(UnlockCountdownDisplay));
        OnPropertyChanged(nameof(TonightUnlockCountDisplay));
        OnPropertyChanged(nameof(RuleSummaryDisplay));
        OnPropertyChanged(nameof(TonightUnlockCount));
        OnPropertyChanged(nameof(CanRequestTemporaryUnlock));
        OnPropertyChanged(nameof(CanEditRules));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _timer.Dispose();
        _processBlocker.Dispose();
        RestoreHostsOnExit();
    }
}
