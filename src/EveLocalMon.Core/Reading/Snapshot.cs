namespace EveLocalMon.Core.Reading;

/// <summary>One reader pass over one captured frame.</summary>
/// <param name="Rows">Full and partial rows, top to bottom.</param>
/// <param name="OccupiedFullRows">Full rows whose Category != Empty.</param>
/// <param name="HeaderMemberCount">Null if the header digits could not be read.</param>
public sealed record Snapshot(
    IReadOnlyList<RowResult> Rows,
    int OccupiedFullRows,
    bool HasPartialRowWithContent,
    int? HeaderMemberCount,
    Readability Readability,
    ThreatBreakdown Threats);
