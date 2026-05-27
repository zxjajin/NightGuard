using System.ComponentModel;
using System.Runtime.CompilerServices;
using NightGuard.Models;
using NightGuard.Services;

namespace NightGuard.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _configService;
    private readonly StartupService _startupService = new();
    private string _processText = "";
    private string _allowedProcessText = "";
    private string _domainText = "";
    private string _passwordInput = "";
    private string _settingsPasswordInput = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public RestrictionEngine Engine { get; }
    public AppConfig EditableConfig { get; private set; }

    public string ProcessText
    {
        get => _processText;
        set { _processText = value; OnPropertyChanged(); }
    }

    public string AllowedProcessText
    {
        get => _allowedProcessText;
        set { _allowedProcessText = value; OnPropertyChanged(); }
    }

    public string DomainText
    {
        get => _domainText;
        set { _domainText = value; OnPropertyChanged(); }
    }

    public string PasswordInput
    {
        get => _passwordInput;
        set { _passwordInput = value; OnPropertyChanged(); }
    }

    public string SettingsPasswordInput
    {
        get => _settingsPasswordInput;
        set { _settingsPasswordInput = value; OnPropertyChanged(); }
    }

    public bool SettingsUnlocked { get; private set; }
    public bool SettingsEditingEnabled => SettingsUnlocked && Engine.CanEditRules;
    public string SettingsMessage { get; private set; } = "输入设置密码后可修改规则。首次使用密码为空，直接解锁即可。";

    public MainViewModel(ConfigService configService, RestrictionEngine engine)
    {
        _configService = configService;
        Engine = engine;
        EditableConfig = Clone(engine.Config);
        EditableConfig.StartWithWindows = _startupService.IsEnabled();
        HydrateTextFields();
        Engine.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Engine));
            OnPropertyChanged(nameof(SettingsEditingEnabled));
        };
    }

    public void UnlockSettings()
    {
        if (!Engine.CanEditRules)
        {
            SettingsMessage = "限制期间禁止修改规则。";
            OnPropertyChanged(nameof(SettingsMessage));
            return;
        }

        SettingsUnlocked = PasswordService.Verify(Engine.Config.SettingsPasswordHash, PasswordInput);
        SettingsMessage = SettingsUnlocked ? "设置已解锁。" : "密码不正确。";
        OnPropertyChanged(nameof(SettingsUnlocked));
        OnPropertyChanged(nameof(SettingsEditingEnabled));
        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void SaveSettings()
    {
        if (!SettingsUnlocked || !Engine.CanEditRules)
        {
            SettingsMessage = "当前不能保存设置。";
            OnPropertyChanged(nameof(SettingsMessage));
            return;
        }

        EditableConfig.BlockedProcesses = SplitLines(ProcessText);
        EditableConfig.AlwaysAllowedProcesses = SplitLines(AllowedProcessText);
        EditableConfig.BlockedDomains = SplitLines(DomainText);
        if (!string.IsNullOrWhiteSpace(SettingsPasswordInput))
        {
            EditableConfig.SettingsPasswordHash = PasswordService.Hash(SettingsPasswordInput);
        }
        else
        {
            EditableConfig.SettingsPasswordHash = Engine.Config.SettingsPasswordHash;
        }

        _configService.Save(EditableConfig);
        _startupService.SetEnabled(EditableConfig.StartWithWindows);
        Engine.ReloadConfig();
        EditableConfig = Clone(Engine.Config);
        EditableConfig.StartWithWindows = _startupService.IsEnabled();
        SettingsPasswordInput = "";
        HydrateTextFields();
        SettingsMessage = "设置已保存。";
        OnPropertyChanged(nameof(EditableConfig));
        OnPropertyChanged(nameof(SettingsEditingEnabled));
        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void RequestTemporaryUnlock() => Engine.RequestTemporaryUnlock();

    private void HydrateTextFields()
    {
        ProcessText = string.Join(Environment.NewLine, EditableConfig.BlockedProcesses);
        AllowedProcessText = string.Join(Environment.NewLine, EditableConfig.AlwaysAllowedProcesses);
        DomainText = string.Join(Environment.NewLine, EditableConfig.BlockedDomains);
    }

    private static List<string> SplitLines(string value)
    {
        return value.Split([Environment.NewLine, "\n", "\r", ",", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AppConfig Clone(AppConfig config)
    {
        return new AppConfig
        {
            RestrictionStart = config.RestrictionStart,
            RestrictionEnd = config.RestrictionEnd,
            BlockAllAppsDuringRestriction = config.BlockAllAppsDuringRestriction,
            AlwaysAllowedProcesses = [.. config.AlwaysAllowedProcesses],
            BlockedProcesses = [.. config.BlockedProcesses],
            BlockedDomains = [.. config.BlockedDomains],
            UnlockDelayMinutes = config.UnlockDelayMinutes,
            TemporaryAllowanceMinutes = config.TemporaryAllowanceMinutes,
            MaxUnlocksPerNight = config.MaxUnlocksPerNight,
            SettingsPasswordHash = config.SettingsPasswordHash,
            StartWithWindows = config.StartWithWindows
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
