namespace NightGuard.Models;

public sealed class ExplorerAccessRequest
{
    public string TargetName { get; set; } = "";
    public ExplorerAccessMode Mode { get; set; }
    public int RemainingWrapUps { get; set; }
    public bool CanAllowWrapUp { get; set; } = true;
}
