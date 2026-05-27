using System.Windows;
using NightGuard.Models;

namespace NightGuard;

public partial class AppAccessWindow : Window
{
    public AppAccessChoice Choice { get; private set; } = AppAccessChoice.Deny;

    public AppAccessWindow(string processName)
    {
        InitializeComponent();
        MessageText.Text = $"{processName}.exe 已被 NightGuard 拦截。选择一个临时放行选项后，请重新打开这个应用。";
    }

    private void AllowOneMinute_Click(object sender, RoutedEventArgs e)
    {
        Choice = AppAccessChoice.AllowOneMinute;
        DialogResult = true;
    }

    private void AllowFifteenMinutes_Click(object sender, RoutedEventArgs e)
    {
        Choice = AppAccessChoice.AllowFifteenMinutes;
        DialogResult = true;
    }

    private void AllowTonight_Click(object sender, RoutedEventArgs e)
    {
        Choice = AppAccessChoice.AllowTonight;
        DialogResult = true;
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        Choice = AppAccessChoice.Deny;
        DialogResult = false;
    }
}
