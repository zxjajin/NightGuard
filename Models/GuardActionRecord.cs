namespace NightGuard.Models;

public sealed class GuardActionRecord
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public string Type { get; set; } = "";
    public string Target { get; set; } = "";
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public string Note { get; set; } = "";
    public string TimeText => Time.ToString("HH:mm:ss");
}
