using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Alerts;

/// <summary>
/// Turns an <see cref="AlertEvent"/> into the words the user actually sees. Pure, and the one
/// place wording lives, because of the invariant from doc/PLAN.md §4.6: an unreadable Local must
/// never read as a confirmed threat, and must never read as all-clear.
/// </summary>
public static class AlertMessageFactory
{
    public static AlertMessage Create(AlertEvent alertEvent) => alertEvent switch
    {
        ThreatIncreased threat => CreateThreat(threat),
        LocalClear clear => new AlertMessage(AlertKind.LocalClear, "EVE: Local clear", "No threats visible.", AlertPriority.Low, clear.At),
        ReadabilityChanged readability => CreateReadability(readability),
        MemberCountIncreasedWhileUnreadable joined => new AlertMessage(
            AlertKind.MemberCountWhileUnreadable,
            "EVE: pilots joined, Local unreadable",
            $"Local {joined.Previous} → {joined.Current} while unreadable. New pilots not checked.",
            AlertPriority.High,
            joined.At),
        _ => throw new ArgumentOutOfRangeException(nameof(alertEvent), alertEvent, "Unknown alert event."),
    };

    private static AlertMessage CreateThreat(ThreatIncreased threat)
    {
        string breakdown = DescribeBreakdown(threat.Breakdown);

        return threat.Previous == 0
            ? new AlertMessage(
                AlertKind.ThreatIncreased,
                "EVE: hostile in Local",
                $"{breakdown} — {Plural(threat.Current, "threat")} visible",
                AlertPriority.Urgent,
                threat.At)
            : new AlertMessage(
                AlertKind.ThreatIncreased,
                "EVE: more hostiles in Local",
                $"+{threat.Current - threat.Previous} ({breakdown}) — now {Plural(threat.Current, "threat")}",
                AlertPriority.Urgent,
                threat.At);
    }

    private static AlertMessage CreateReadability(ReadabilityChanged readability)
    {
        if (readability.Current == Readability.Full)
        {
            string body = readability.MemberCount is { } count
                ? $"{Plural(count, "pilot")}, all visible."
                : $"{Plural(readability.VisibleRows, "row")} readable.";
            return new AlertMessage(AlertKind.Readable, "EVE: Local readable again", body, AlertPriority.Low, readability.At);
        }

        string reason = readability.Current switch
        {
            Readability.NotVisible => "Local window not visible — covered, minimised or mis-calibrated.",
            _ => readability.MemberCount is { } count
                ? $"Local too long to read: {count} pilots, {readability.VisibleRows} visible."
                : $"Local too long to read: {readability.VisibleRows} visible, member count unreadable.",
        };

        return new AlertMessage(
            AlertKind.Unreadable,
            "EVE: Local unreadable",
            $"{reason} Cannot confirm Local is clear.",
            AlertPriority.High,
            readability.At);
    }

    /// <summary>"2 red, 1 neutral" — categories with nothing in them are left out entirely.
    /// Pilots with no icon read as neutral (that is what they are to you); an unmatched icon reads
    /// as "unknown icon" to keep "unidentified" distinct from "confirmed hostile".</summary>
    private static string DescribeBreakdown(ThreatBreakdown breakdown)
    {
        var parts = new List<string>(3);

        if (breakdown.Enemy > 0) parts.Add($"{breakdown.Enemy} red");

        int neutrals = breakdown.Neutral + breakdown.ExtraNoIcon;
        if (neutrals > 0) parts.Add($"{neutrals} neutral");

        if (breakdown.Unknown > 0) parts.Add(Plural(breakdown.Unknown, "unknown icon"));

        return parts.Count == 0 ? "no categories identified" : string.Join(", ", parts);
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
