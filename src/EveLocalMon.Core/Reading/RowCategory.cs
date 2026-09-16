namespace EveLocalMon.Core.Reading;

/// <summary>
/// The classification of one Local member-list row.
/// </summary>
public enum RowCategory
{
    /// <summary>No text and no icon: an unoccupied slot below the last pilot.</summary>
    Empty,

    /// <summary>Icon matched a Friendly template.</summary>
    Friendly,

    /// <summary>Icon matched an Enemy template.</summary>
    Enemy,

    /// <summary>Icon matched a Neutral template (the gray neutral-standing icon).</summary>
    Neutral,

    /// <summary>An icon is present but matches no known template. Treated as a threat by default.</summary>
    Unknown,

    /// <summary>Name text present but no icon: a true neutral, or the pilot in the active client.</summary>
    NoIcon,
}
