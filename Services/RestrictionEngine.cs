using System.ComponentModel;
using System.Runtime.CompilerServices;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class RestrictionEngine : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigService _configService;
    private readonly JsonLogService _logService;
    private readonly HostsService _hostsService;
    private readonly ProcessBlockerService _processBlocker;
    private readonly System.Threading.Timer _timer;
    private bool _restrictionApplied;
    private bool _restrictionStartLogged;
    private DateTimeOffset? _unlockRequestedAt;
    private DateTimeOffset? _temporaryAllowedUntil;
    private string _nightKey = DateTime.Today.ToString("yyyy-MM-dd");

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
    public TimeSpan RemainingRestrictionTime { get; private set; }
    public TimeSpan UnlockCountdown { get; private set; }
    public string RemainingRestrictionDisplay => $"今天剩余限制时间：{FormatTimeSpan(RemainingRestrictionTime)}";
    public string UnlockCountdownDisplay => $"倒计时：{FormatTimeSpan(UnlockCountdown)}";
    public string TonightUnlockCountDisplay => $"今晚已临时解锁次数：{TonightUnlockCount}";
    public int TonightUnlockCount { get; private set; }
    public bool CanEditRules => State is GuardState.NotRestricted;
    public bool CanRequestTemporaryUnlock => State is GuardState.Restricted && TonightUnlockCount < Config.MaxUnlocksPerNight;

    public RestrictionEngine(ConfigService configService, JsonLogService logService, HostsService hostsService, ProcessBlockerService processBlocker)
    {
        _configService = configService;
        _logService = logService;
        _hostsService = hostsService;
        _processBlocker = processBlocker;
        Config = _configService.Load();
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void ReloadConfig()
    {
        Config = _configService.Load();
        var window = GetCurrentRestrictionWindow(DateTimeOffset.Now);
        if (_restrictionApplied && window is not null)
        {
            _processBlocker.UpdateConfig(Config, _nightKey, window.Value.End);
        }

        OnPropertyChanged(nameof(Config));
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
            _hostsService.Restore(_nightKey);
        }
        catch
        {
            // Best-effort cleanup on exit. Lack of administrator rights can still prevent hosts writes.
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        var window = GetCurrentRestrictionWindow(now);
        var inRestrictionWindow = window is not null;
        _nightKey = GetNightKey(now);
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

    private void EnterRestrictionMode(DateTimeOffset restrictionEndsAt)
    {
        if (_restrictionApplied)
        {
            _processBlocker.UpdateConfig(Config, _nightKey, restrictionEndsAt);
            return;
        }

        try
        {
            _hostsService.Apply(Config, _nightKey);
        }
        catch (Exception ex)
        {
            _logService.RecordSystemMessage(_nightKey, $"failed to apply hosts block: {ex.Message}");
        }

        _processBlocker.Start(Config, _nightKey, restrictionEndsAt);
        _restrictionApplied = true;
    }

    private void ExitRestrictionMode()
    {
        if (!_restrictionApplied)
        {
            return;
        }

        _processBlocker.Stop();
        try
        {
            _hostsService.Restore(_nightKey);
        }
        catch (Exception ex)
        {
            _logService.RecordSystemMessage(_nightKey, $"failed to restore hosts: {ex.Message}");
        }

        _restrictionApplied = false;
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

    private void OnAllStatusChanged()
    {
        OnPropertyChanged(nameof(RemainingRestrictionTime));
        OnPropertyChanged(nameof(UnlockCountdown));
        OnPropertyChanged(nameof(RemainingRestrictionDisplay));
        OnPropertyChanged(nameof(UnlockCountdownDisplay));
        OnPropertyChanged(nameof(TonightUnlockCountDisplay));
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
