using System.Windows;
using NightGuard.Models;

namespace NightGuard;

public partial class AppAccessWindow : Window
{
    private readonly ExplorerAccessMode _mode;

    public AppAccessChoice Choice { get; private set; } = AppAccessChoice.Deny;

    public AppAccessWindow(ExplorerAccessRequest request)
    {
        InitializeComponent();
        _mode = request.Mode;

        if (request.Mode == ExplorerAccessMode.ReminderOnly)
        {
            TitleText.Text = "夜间收尾提醒";
            MessageText.Text = $"{request.TargetName} 已进入夜间收尾提醒时段。建议不要开启新问题，只做保存、整理、记录明天任务。";
            PrimaryButton.Visibility = Visibility.Collapsed;
            SecondaryButton.Visibility = Visibility.Collapsed;
            TertiaryButton.Visibility = Visibility.Collapsed;
            CloseButtonAction.Content = "知道了";
            return;
        }

        TitleText.Text = "夜间收尾提醒";
        MessageText.Text = $"{request.TargetName} 已进入夜间收尾时段。建议只做必要收尾，或把想继续探索的内容记录到明天。";
        PrimaryButton.Content = "本晚不再限制";
        SecondaryButton.Content = "记录到明天并最小化";
        TertiaryButton.Content = "稍后再提醒我";
        CloseButtonAction.Visibility = Visibility.Collapsed;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Choice = _mode == ExplorerAccessMode.LimitedWrapUp ? AppAccessChoice.AllowTonight : AppAccessChoice.Deny;
        DialogResult = true;
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Choice = _mode == ExplorerAccessMode.LimitedWrapUp ? AppAccessChoice.RecordForTomorrow : AppAccessChoice.Deny;
        DialogResult = true;
    }

    private void Tertiary_Click(object sender, RoutedEventArgs e)
    {
        Choice = _mode == ExplorerAccessMode.LimitedWrapUp ? AppAccessChoice.RemindLater : AppAccessChoice.Deny;
        DialogResult = true;
    }

    private void CloseButtonAction_Click(object sender, RoutedEventArgs e)
    {
        Choice = AppAccessChoice.Deny;
        DialogResult = true;
    }
}
