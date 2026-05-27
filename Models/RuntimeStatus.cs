namespace NightGuard.Models;

public enum GuardState
{
    NotRestricted,
    Restricted,
    WaitingForTemporaryUnlock,
    TemporaryAllowed
}
