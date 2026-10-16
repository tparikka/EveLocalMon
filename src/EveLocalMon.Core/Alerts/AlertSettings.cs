namespace EveLocalMon.Core.Alerts;

public sealed record AlertChannelSet(bool Sound, bool Popup, bool Ntfy);

public sealed record AlertSettings
{
    /// <summary>A confirmed hostile/neutral/unknown appeared or the count rose.</summary>
    public AlertChannelSet Threats { get; init; } = new(Sound: true, Popup: true, Ntfy: true);

    /// <summary>Local can't be read in full (overflow, or the window isn't visible). The user chose
    /// to treat "cannot confirm Local is clear" as seriously as a confirmed threat, ntfy included.</summary>
    public AlertChannelSet Unreadable { get; init; } = new(Sound: true, Popup: true, Ntfy: true);

    /// <summary>Local went clear, or became readable again. Quiet by default — good news doesn't
    /// need to interrupt.</summary>
    public AlertChannelSet AllClear { get; init; } = new(Sound: false, Popup: true, Ntfy: false);

    public int VolumePercent { get; init; } = 70;

    /// <summary>Overrides the built-in tone. The volume setting only applies to the built-in tone.</summary>
    public string? SoundFilePath { get; init; }

    public string NtfyServer { get; init; } = "https://ntfy.sh";

    /// <summary>Anyone who knows an ntfy.sh topic can read it, so this is generated at random when
    /// the user first enables the channel rather than defaulted to something guessable.</summary>
    public string? NtfyTopic { get; init; }

    public int SoundMinIntervalMs { get; init; } = 3000;
    public int PopupMinIntervalMs { get; init; } = 1000;
    public int NtfyMinIntervalMs { get; init; } = 15000;

    public AlertChannelSet ChannelsFor(AlertKind kind) => kind switch
    {
        AlertKind.ThreatIncreased => Threats,
        AlertKind.Unreadable or AlertKind.MemberCountWhileUnreadable => Unreadable,
        _ => AllClear,
    };
}
