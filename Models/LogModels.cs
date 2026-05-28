namespace NightGuard.Models;

public sealed class AppLogStore
{
    public List<DailyLog> Days { get; set; } = [];
}

public sealed class DailyLog
{
    public string Date { get; set; } = "";
    public List<string> RestrictionStartedAt { get; set; } = [];
    public List<BlockedProcessLog> BlockedProcesses { get; set; } = [];
    public int TemporaryUnlockCount { get; set; }
    public List<TemporaryUnlockLog> TemporaryUnlocks { get; set; } = [];
    public List<string> SystemMessages { get; set; } = [];
    public int NightFocusWrapUpCount { get; set; }
    public List<string> NightFocusReminderTargets { get; set; } = [];
}

public sealed class BlockedProcessLog
{
    public DateTimeOffset Time { get; set; }
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
}

public sealed class TemporaryUnlockLog
{
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}
