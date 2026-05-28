namespace NightGuard.Models;

public sealed class RuleTemplate
{
    public string Name { get; set; } = "";
    public List<string> ProcessNames { get; set; } = [];
    public List<string> Domains { get; set; } = [];
    public string Note { get; set; } = "";
}
