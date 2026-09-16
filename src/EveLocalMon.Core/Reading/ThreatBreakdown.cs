namespace EveLocalMon.Core.Reading;

/// <param name="ExtraNoIcon">No-icon rows beyond the allowance for the active client's own pilot
/// (see <see cref="ReaderOptions.OwnPilotNoIconAllowance"/>) — i.e. other pilots with no standing icon.</param>
public sealed record ThreatBreakdown(int Enemy, int Neutral, int Unknown, int ExtraNoIcon)
{
    public int Total => Enemy + Neutral + Unknown + ExtraNoIcon;
}
