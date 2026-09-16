namespace EveLocalMon.Core.Reading;

/// <summary>
/// Whether the current frame gives full visibility of the Local member list.
/// See doc/PLAN.md §4.6.
/// </summary>
public enum Readability
{
    /// <summary>Every member is visible in a full (non-partial) row.</summary>
    Full,

    /// <summary>Local has more pilots than fit on screen (partial row, or header count exceeds visible rows).</summary>
    Overflow,

    /// <summary>No occupied rows at all. The active client's own pilot is always listed, so this
    /// means the Local window is covered, minimised, moved, or the calibration is wrong —
    /// not that the system is genuinely empty.</summary>
    NotVisible,
}
