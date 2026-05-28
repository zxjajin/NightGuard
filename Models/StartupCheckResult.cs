namespace NightGuard.Models;

public sealed class StartupCheckResult
{
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.Now;
    public CheckItem Administrator { get; set; } = new();
    public CheckItem HostsWritable { get; set; } = new();
    public CheckItem StartupTask { get; set; } = new();
    public CheckItem RestrictionActive { get; set; } = new();
    public string CheckedAtText => $"检测时间：{CheckedAt:HH:mm:ss}";
}

public sealed class CheckItem
{
    public string Name { get; set; } = "";
    public bool IsOk { get; set; }
    public string StatusText { get; set; } = "";
    public string Suggestion { get; set; } = "";
}
