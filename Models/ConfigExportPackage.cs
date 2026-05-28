namespace NightGuard.Models;

public sealed class ConfigExportPackage
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public List<string> AppRules { get; set; } = [];
    public List<string> HostRules { get; set; } = [];
    public AppConfig Settings { get; set; } = new();
}
