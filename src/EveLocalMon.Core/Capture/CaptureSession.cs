using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Capture;

public enum CaptureDecision
{
    Save,
    SkipDuplicate,
}

public sealed record CaptureSessionOptions
{
    /// <summary>Stop after this many frames have been <b>saved</b>. Null runs until stopped.
    /// Counting saves rather than attempts is what "give me 5 screenshots" actually means —
    /// the old CLI counted attempts and silently returned fewer files (doc/M2-SPEC.md §6).</summary>
    public int? MaxSavedFrames { get; init; }

    /// <summary>Skip frames byte-identical to the last saved one.</summary>
    public bool Dedupe { get; init; } = true;
}

public sealed record CaptureOutcome(CaptureDecision Decision, int SkippedBefore);

/// <summary>
/// The save/skip/stop bookkeeping of a capture run, with no console or OS dependency so it can
/// be unit tested. The caller does the actual capturing and file writing.
/// </summary>
public sealed class CaptureSession(CaptureSessionOptions options)
{
    private byte[]? _lastSaved;
    private int _consecutiveSkipped;

    public int FramesCaptured { get; private set; }
    public int FramesSaved { get; private set; }
    public int FramesSkipped { get; private set; }

    public bool IsComplete => options.MaxSavedFrames is { } max && FramesSaved >= max;

    /// <summary>Records a captured frame and says whether the caller should save it. When the
    /// answer is Save, <see cref="CaptureOutcome.SkippedBefore"/> reports how many duplicates were
    /// dropped since the previous save, so gaps in file timestamps can explain themselves.</summary>
    public CaptureOutcome Offer(Frame frame)
    {
        FramesCaptured++;

        if (options.Dedupe && _lastSaved is not null && frame.Pixels.AsSpan().SequenceEqual(_lastSaved))
        {
            FramesSkipped++;
            _consecutiveSkipped++;
            return new CaptureOutcome(CaptureDecision.SkipDuplicate, _consecutiveSkipped);
        }

        int skippedBefore = _consecutiveSkipped;
        _consecutiveSkipped = 0;
        _lastSaved = frame.Pixels;
        FramesSaved++;
        return new CaptureOutcome(CaptureDecision.Save, skippedBefore);
    }

    /// <summary>Duplicates dropped since the last save — the tail that no save will report.</summary>
    public int PendingSkipped => _consecutiveSkipped;
}
