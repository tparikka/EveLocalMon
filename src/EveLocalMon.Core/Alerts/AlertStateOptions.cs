namespace EveLocalMon.Core.Alerts;

public sealed record AlertStateOptions
{
    /// <summary>How many consecutive identical readings a raw value needs before it becomes "stable"
    /// and can trigger an event. Filters scroll/redraw glitches and icon-flash flicker.</summary>
    public int StableFrames { get; init; } = 2;

    /// <summary>Emit <see cref="LocalClear"/> when the stable threat count returns to 0.</summary>
    public bool EmitLocalClear { get; init; } = true;

    /// <summary>Emit <see cref="MemberCountIncreasedWhileUnreadable"/> when the header member count
    /// rises while the list is not fully readable. Off by default: in a busy, friendly system
    /// this would fire constantly and carries no confirmed threat information.</summary>
    public bool AlertOnMemberIncreaseWhileUnreadable { get; init; } = false;
}
