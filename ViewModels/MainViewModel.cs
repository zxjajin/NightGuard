using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using NightGuard.Models;
using NightGuard.Services;

namespace NightGuard.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _configService;
    private readonly StartupService _startupService = new();
    private readonly StartupCheckService _startupCheckService;
    private readonly GuardActionRecordService _recordService;
    private readonly ConfigImportExportService _configImportExportService = new();
    private string _processText = "";
    private string _allowedProcessText = "";
    private string _domainText = "";
    private string _passwordInput = "";
    private string _settingsPasswordInput = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public RestrictionEngine Engine { get; }
    public AppConfig EditableConfig { get; private set; }
    public ObservableCollection<GuardActionRecord> RecentRecords { get; }
    public IReadOnlyList<RuleTemplate> RuleTemplates { get; }
    public StartupCheckResult StartupCheck { get; private set; }

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

    public MainViewModel(ConfigService configService, RestrictionEngine engine, GuardActionRecordService recordService)
    {
        _configService = configService;
        _recordService = recordService;
        _startupCheckService = new StartupCheckService(_startupService);
        Engine = engine;
        RecentRecords = [];
        recordService.Records.CollectionChanged += Records_CollectionChanged;
        RefreshRecentRecords();
        RuleTemplates = new RuleTemplateService().GetTemplates();
        EditableConfig = Clone(engine.Config);
        EditableConfig.StartWithWindows = _startupService.IsEnabled();
        StartupCheck = _startupCheckService.Check(Engine.State == GuardState.Restricted);
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
            SettingsMessage = "当前不能保存设置。限制期间请先退出限制模式。";
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
        SettingsMessage = "设置已保存。应用白名单、应用黑名单、网站黑名单已立即生效；如果刚更新程序代码，请重启 NightGuard。";
        OnPropertyChanged(nameof(EditableConfig));
        OnPropertyChanged(nameof(SettingsEditingEnabled));
        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void RequestTemporaryUnlock() => Engine.RequestTemporaryUnlock();

    public void StartTestMode()
    {
        Engine.StartTestMode(TimeSpan.FromMinutes(3));
    }

    public void RunStartupCheck()
    {
        StartupCheck = _startupCheckService.Check(Engine.State != GuardState.NotRestricted);
        OnPropertyChanged(nameof(StartupCheck));
    }

    public void RestoreHostsNow()
    {
        try
        {
            Engine.RestoreHostsNow();
            SettingsMessage = "已立即恢复 hosts，并移除 NightGuard 写入的 hosts 区块。";
        }
        catch (Exception ex)
        {
            SettingsMessage = $"恢复 hosts 失败：{ex.Message}。请确认 NightGuard 是管理员权限运行。";
        }

        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void ApplyRuleTemplate(RuleTemplate template)
    {
        if (!SettingsEditingEnabled)
        {
            SettingsMessage = "请先解锁设置，并确认当前不在限制期间。";
            OnPropertyChanged(nameof(SettingsMessage));
            return;
        }

        var processBefore = EditableConfig.BlockedProcesses.Count;
        var domainBefore = EditableConfig.BlockedDomains.Count;
        var processSet = EditableConfig.BlockedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domainSet = EditableConfig.BlockedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var process in template.ProcessNames.Select(NormalizeRuleValue).Where(item => item.Length > 0))
        {
            if (processSet.Add(process))
            {
                EditableConfig.BlockedProcesses.Add(process);
            }
        }

        foreach (var domain in template.Domains.Select(NormalizeRuleValue).Where(item => item.Length > 0))
        {
            if (domainSet.Add(domain))
            {
                EditableConfig.BlockedDomains.Add(domain);
            }
        }

        _configService.Save(EditableConfig);
        Engine.ReloadConfig();
        EditableConfig = Clone(Engine.Config);
        HydrateTextFields();

        var addedProcesses = EditableConfig.BlockedProcesses.Count - processBefore;
        var addedDomains = EditableConfig.BlockedDomains.Count - domainBefore;
        var skipped = template.ProcessNames.Count + template.Domains.Count - addedProcesses - addedDomains;
        SettingsMessage = $"已添加模板“{template.Name}”：新增进程规则 {addedProcesses} 条，新增网站规则 {addedDomains} 条，跳过重复 {skipped} 条。";
        _recordService.Add("规则模板", template.Name, "添加模板", "成功", SettingsMessage);
        OnPropertyChanged(nameof(EditableConfig));
        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void ExportConfig(string path)
    {
        _configImportExportService.Export(Engine.Config, path);
        SettingsMessage = $"配置已导出：{path}";
        _recordService.Add("配置导出", path, "导出配置", "成功", "JSON 配置备份");
        OnPropertyChanged(nameof(SettingsMessage));
    }

    public void ImportConfig(string path)
    {
        var imported = _configImportExportService.Import(path);
        _configService.Save(imported);
        Engine.ReloadConfig();
        EditableConfig = Clone(Engine.Config);
        EditableConfig.StartWithWindows = _startupService.IsEnabled();
        HydrateTextFields();
        SettingsMessage = $"配置已导入：{path}";
        _recordService.Add("配置导入", path, "导入配置", "成功", "页面数据已刷新");
        OnPropertyChanged(nameof(EditableConfig));
        OnPropertyChanged(nameof(SettingsMessage));
    }

    private void HydrateTextFields()
    {
        ProcessText = string.Join(Environment.NewLine, EditableConfig.BlockedProcesses);
        AllowedProcessText = string.Join(Environment.NewLine, EditableConfig.AlwaysAllowedProcesses);
        DomainText = string.Join(Environment.NewLine, EditableConfig.BlockedDomains);
    }

    private void Records_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshRecentRecords();
    }

    private void RefreshRecentRecords()
    {
        RecentRecords.Clear();
        foreach (var record in _recordService.Records.Take(20))
        {
            RecentRecords.Add(record);
        }
    }

    private static List<string> SplitLines(string value)
    {
        return value.Split([Environment.NewLine, "\n", "\r", ",", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRuleValue(string value)
    {
        return value.Trim();
    }

    private static AppConfig Clone(AppConfig config)
    {
        return new AppConfig
        {
            RestrictionStart = config.RestrictionStart,
            RestrictionEnd = config.RestrictionEnd,
            BlockAllAppsDuringRestriction = config.BlockAllAppsDuringRestriction,
            EnableHostsBlocking = config.EnableHostsBlocking,
            AlwaysAllowedProcesses = [.. config.AlwaysAllowedProcesses],
            AlwaysAllowedDomains = [.. config.AlwaysAllowedDomains],
            BlockedProcesses = [.. config.BlockedProcesses],
            BlockedDomains = [.. config.BlockedDomains],
            UnlockDelayMinutes = config.UnlockDelayMinutes,
            TemporaryAllowanceMinutes = config.TemporaryAllowanceMinutes,
            MaxUnlocksPerNight = config.MaxUnlocksPerNight,
            SettingsPasswordHash = config.SettingsPasswordHash,
            StartWithWindows = config.StartWithWindows,
            NightFocusProcesses = [.. config.NightFocusProcesses],
            NightFocusDomains = [.. config.NightFocusDomains]
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
