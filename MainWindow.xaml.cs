using System.ComponentModel;
using System.Windows;
using NightGuard.Models;
using NightGuard.Services;
using NightGuard.ViewModels;
using Forms = System.Windows.Forms;

namespace NightGuard;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExiting;
    private bool _cleanedUp;

    public MainWindow()
    {
        InitializeComponent();

        var configService = new ConfigService();
        var logService = new JsonLogService(configService.DataDirectory);
        var recordService = new GuardActionRecordService();
        var hostsService = new HostsService(configService.DataDirectory, logService, recordService);
        var processBlocker = new ProcessBlockerService(logService, recordService);
        processBlocker.AccessRequested = RequestAppAccess;
        var engine = new RestrictionEngine(configService, logService, hostsService, processBlocker, recordService);
        engine.RestrictionStarted += (_, _) => ShowRestrictionStartedReminder();

        _viewModel = new MainViewModel(configService, engine, recordService);
        DataContext = _viewModel;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "NightGuard",
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    public void CleanupBeforeExit()
    {
        if (_cleanedUp)
        {
            return;
        }

        _viewModel.Engine.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _cleanedUp = true;
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => ShowFromTray());
        menu.Items.Add("退出并恢复 hosts", null, (_, _) =>
        {
            _isExiting = true;
            CleanupBeforeExit();
            System.Windows.Application.Current.Shutdown();
        });
        return menu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void TemporaryUnlock_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestTemporaryUnlock();
    }

    private void StartTestMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StartTestMode();
    }

    private void RunStartupCheck_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RunStartupCheck();
    }

    private void UnlockSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PasswordInput = SettingsPasswordBox.Password;
        _viewModel.UnlockSettings();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SettingsPasswordInput = NewPasswordBox.Password;
        _viewModel.SaveSettings();
        NewPasswordBox.Clear();
    }

    private void RestoreHosts_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RestoreHostsNow();
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RuleTemplate template })
        {
            _viewModel.ApplyRuleTemplate(template);
        }
    }

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 NightGuard 配置",
            Filter = "JSON 配置文件 (*.json)|*.json",
            FileName = $"NightGuard_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportConfig(dialog.FileName);
        }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 NightGuard 配置",
            Filter = "JSON 配置文件 (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "导入配置会覆盖当前设置，确定继续吗？",
            "确认导入配置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.ImportConfig(dialog.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"导入失败：{ex.Message}", "导入配置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private AppAccessChoice RequestAppAccess(ExplorerAccessRequest request)
    {
        return Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            var dialog = new AppAccessWindow(request)
            {
                Owner = this
            };

            dialog.ShowDialog();
            return dialog.Choice;
        });
    }

    private void ShowRestrictionStartedReminder()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowFromTray();
            System.Windows.MessageBox.Show(
                this,
                "NightGuard 已进入限制模式。\n\n夜间探索型工具会在 23:30 - 07:00 进入收束提醒，可选择本晚不再限制、记录到明天并最小化，或 15 分钟后再提醒。",
                "NightGuard 限制开始",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }
}
