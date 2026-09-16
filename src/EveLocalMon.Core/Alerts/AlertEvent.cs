using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Alerts;

public abstract record AlertEvent(DateTimeOffset At);

/// <summary>The stable threat count rose. Covers both a first appearance (Previous 0) and a
/// further increase while already occupied.</summary>
public sealed record ThreatIncreased(int Previous, int Current, ThreatBreakdown Breakdown, DateTimeOffset At) : AlertEvent(At);

/// <summary>The stable threat count fell back to 0 from above 0.</summary>
public sealed record LocalClear(DateTimeOffset At) : AlertEvent(At);

/// <summary>The stable <see cref="Readability"/> changed. The dispatcher must word Overflow/NotVisible
/// as "Local unreadable", never as a confirmed threat.</summary>
public sealed record ReadabilityChanged(Readability Previous, Readability Current, int? MemberCount, int VisibleRows, DateTimeOffset At) : AlertEvent(At);

/// <summary>The header member count rose while the list was not fully readable. Only emitted
/// when <see cref="AlertStateOptions.AlertOnMemberIncreaseWhileUnreadable"/> is set.</summary>
public sealed record MemberCountIncreasedWhileUnreadable(int Previous, int Current, DateTimeOffset At) : AlertEvent(At);
