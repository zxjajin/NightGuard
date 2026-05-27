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
        var hostsService = new HostsService(configService.DataDirectory, logService);
        var processBlocker = new ProcessBlockerService(logService);
        processBlocker.AccessRequested = RequestAppAccess;
        var engine = new RestrictionEngine(configService, logService, hostsService, processBlocker);
        engine.RestrictionStarted += (_, _) => ShowRestrictionStartedReminder();

        _viewModel = new MainViewModel(configService, engine);
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

    private AppAccessChoice RequestAppAccess(string processName)
    {
        return Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            var dialog = new AppAccessWindow(processName)
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
                "已到达限制时间，NightGuard 已进入限制模式。\n\n非白名单应用将被禁用；打开被限制应用时可以选择允许 1 分钟、15 分钟或今晚不限。",
                "NightGuard 限制已开始",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }
}
