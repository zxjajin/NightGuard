namespace NightGuard.Models;

public enum AppAccessChoice
{
    Deny,
    AllowOneMinute,
    AllowTenMinutes,
    AllowFifteenMinutes,
    AllowTonight,
    RecordForTomorrow,
    RemindLater
}
