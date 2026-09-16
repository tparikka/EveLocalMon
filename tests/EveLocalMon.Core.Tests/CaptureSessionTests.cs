using EveLocalMon.Core.Capture;
using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Tests;

/// <summary>Covers the behaviour that made a `--count 5` run produce 4 files with no explanation:
/// counting attempts instead of saves, and dropping duplicates silently (doc/M2-SPEC.md §6).</summary>
public class CaptureSessionTests
{
    private static Frame FrameOf(byte value)
    {
        var pixels = new byte[4 * 4 * 4];
        Array.Fill(pixels, value);
        return new Frame(4, 4, 16, pixels);
    }

    [Fact]
    public void CountStopsAfterThatManySavedFramesNotThatManyAttempts()
    {
        var session = new CaptureSession(new CaptureSessionOptions { MaxSavedFrames = 5 });

        // Frame 3 repeats, so a 5-save run has to take 6 captures.
        byte[] sequence = [1, 2, 3, 3, 4, 5];
        var decisions = new List<CaptureDecision>();
        foreach (byte value in sequence)
        {
            decisions.Add(session.Offer(FrameOf(value)).Decision);
            if (session.IsComplete) break;
        }

        Assert.Equal(6, session.FramesCaptured);
        Assert.Equal(5, session.FramesSaved);
        Assert.Equal(1, session.FramesSkipped);
        Assert.True(session.IsComplete);
        Assert.Equal(CaptureDecision.SkipDuplicate, decisions[3]);
    }

    [Fact]
    public void SkippedDuplicatesAreReportedWithTheNextSave()
    {
        var session = new CaptureSession(new CaptureSessionOptions());

        session.Offer(FrameOf(1));
        session.Offer(FrameOf(1));
        session.Offer(FrameOf(1));
        CaptureOutcome save = session.Offer(FrameOf(2));

        Assert.Equal(CaptureDecision.Save, save.Decision);
        Assert.Equal(2, save.SkippedBefore);
        Assert.Equal(0, session.PendingSkipped);
    }

    [Fact]
    public void TrailingDuplicatesAreStillCountedForTheSummary()
    {
        var session = new CaptureSession(new CaptureSessionOptions());

        session.Offer(FrameOf(1));
        session.Offer(FrameOf(1));
        session.Offer(FrameOf(1));

        Assert.Equal(2, session.PendingSkipped);
        Assert.Equal(3, session.FramesCaptured);
        Assert.Equal(1, session.FramesSaved);
        Assert.Equal(2, session.FramesSkipped);
    }

    [Fact]
    public void NoDedupeSavesEveryFrame()
    {
        var session = new CaptureSession(new CaptureSessionOptions { Dedupe = false });

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(CaptureDecision.Save, session.Offer(FrameOf(7)).Decision);
        }

        Assert.Equal(4, session.FramesSaved);
        Assert.Equal(0, session.FramesSkipped);
    }

    [Fact]
    public void WithoutACountTheSessionNeverCompletesOnItsOwn()
    {
        var session = new CaptureSession(new CaptureSessionOptions());

        for (int i = 0; i < 50; i++)
        {
            session.Offer(FrameOf((byte)i));
        }

        Assert.False(session.IsComplete);
        Assert.Equal(session.FramesCaptured, session.FramesSaved + session.FramesSkipped);
    }
}
