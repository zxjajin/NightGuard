namespace NightGuard;

public partial class App : System.Windows.Application
{
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.CleanupBeforeExit();
        }

        base.OnExit(e);
    }
}
