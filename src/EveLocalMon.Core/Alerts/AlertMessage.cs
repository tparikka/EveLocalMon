namespace EveLocalMon.Core.Alerts;

public enum AlertKind
{
    ThreatIncreased,
    LocalClear,
    Unreadable,
    Readable,
    MemberCountWhileUnreadable,
}

public enum AlertPriority
{
    Low,
    High,
    Urgent,
}

public sealed record AlertMessage(AlertKind Kind, string Title, string Body, AlertPriority Priority, DateTimeOffset At);
