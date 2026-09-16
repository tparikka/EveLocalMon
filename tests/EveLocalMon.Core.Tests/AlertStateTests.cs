using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

public class AlertStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Snapshot MakeSnapshot(int threatTotal, Readability readability = Readability.Full, int? headerCount = null) =>
        new(
            Rows: [],
            OccupiedFullRows: 1,
            HasPartialRowWithContent: false,
            HeaderMemberCount: headerCount,
            Readability: readability,
            Threats: new ThreatBreakdown(Enemy: threatTotal, Neutral: 0, Unknown: 0, ExtraNoIcon: 0));

    [Fact]
    public void ThreatIncrease_FiresOnceStableAcrossTwoFrames()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2 });

        Assert.Empty(state.Update(MakeSnapshot(0), Now));
        Assert.Empty(state.Update(MakeSnapshot(0), Now));
        Assert.Empty(state.Update(MakeSnapshot(1), Now));
        AlertEvent evt = Assert.Single(state.Update(MakeSnapshot(1), Now));

        var increased = Assert.IsType<ThreatIncreased>(evt);
        Assert.Equal(0, increased.Previous);
        Assert.Equal(1, increased.Current);
    }

    [Fact]
    public void ThreatFlicker_NeverStabilizesSoNeverAlerts()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2 });
        int[] sequence = [1, 2, 1, 2, 1, 2];

        var allEvents = sequence.SelectMany(v => state.Update(MakeSnapshot(v), Now)).ToList();

        Assert.Empty(allEvents);
    }

    [Fact]
    public void ThreatIncrease_FiresForEachStableStep()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2 });
        int[] sequence = [1, 1, 2, 2];

        List<AlertEvent> events = sequence.SelectMany(v => state.Update(MakeSnapshot(v), Now)).ToList();

        Assert.Equal(2, events.Count);
        var first = Assert.IsType<ThreatIncreased>(events[0]);
        Assert.Equal((0, 1), (first.Previous, first.Current));
        var second = Assert.IsType<ThreatIncreased>(events[1]);
        Assert.Equal((1, 2), (second.Previous, second.Current));
    }

    [Fact]
    public void LocalClear_FiresWhenThreatsReturnToZero()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2, EmitLocalClear = true });

        state.Update(MakeSnapshot(2), Now);
        state.Update(MakeSnapshot(2), Now);
        state.Update(MakeSnapshot(0), Now);
        List<AlertEvent> events = state.Update(MakeSnapshot(0), Now).ToList();

        Assert.Contains(events, e => e is LocalClear);
    }

    [Fact]
    public void LocalClear_SuppressedWhenDisabled()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2, EmitLocalClear = false });

        state.Update(MakeSnapshot(2), Now);
        state.Update(MakeSnapshot(2), Now);
        state.Update(MakeSnapshot(0), Now);
        List<AlertEvent> events = state.Update(MakeSnapshot(0), Now).ToList();

        Assert.DoesNotContain(events, e => e is LocalClear);
    }

    [Fact]
    public void ReadabilityChanged_FiresOnceAfterStabilizing()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2 });

        var all = new List<AlertEvent>();
        all.AddRange(state.Update(MakeSnapshot(0, Readability.Full), Now));
        all.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow), Now));
        all.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow), Now));

        ReadabilityChanged changed = Assert.Single(all.OfType<ReadabilityChanged>());
        Assert.Equal(Readability.Full, changed.Previous);
        Assert.Equal(Readability.Overflow, changed.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberCountIncreaseWhileUnreadable_OnlyFiresWhenEnabled(bool enabled)
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2, AlertOnMemberIncreaseWhileUnreadable = enabled });

        var events = new List<AlertEvent>();
        events.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow, 100), Now));
        events.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow, 100), Now));
        events.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow, 103), Now));
        events.AddRange(state.Update(MakeSnapshot(0, Readability.Overflow, 103), Now));

        bool fired = events.OfType<MemberCountIncreasedWhileUnreadable>().Any(e => e is { Previous: 100, Current: 103 });
        Assert.Equal(enabled, fired);
    }

    [Fact]
    public void NullMemberCountReadingDoesNotResetProgressTowardStabilizing()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 2, AlertOnMemberIncreaseWhileUnreadable = true });

        // 100 (1/2), then a dropout reading (ignored, not reset), then 100 again should still
        // only need one more matching frame to stabilize — not restart from scratch.
        state.Update(MakeSnapshot(0, Readability.Overflow, 100), Now);
        state.Update(MakeSnapshot(0, Readability.Overflow, null), Now);
        state.Update(MakeSnapshot(0, Readability.Overflow, 100), Now);

        // Stabilized at 100 with no prior baseline, so no increase event yet.
        var afterStabilizing = state.Update(MakeSnapshot(0, Readability.Overflow, 103), Now)
            .Concat(state.Update(MakeSnapshot(0, Readability.Overflow, 103), Now));

        Assert.Contains(afterStabilizing, e => e is MemberCountIncreasedWhileUnreadable { Previous: 100, Current: 103 });
    }

    [Fact]
    public void StartupWithThreatsAlreadyPresent_FiresAfterStableFrames()
    {
        var state = new AlertState(new AlertStateOptions { StableFrames = 3 });

        Assert.Empty(state.Update(MakeSnapshot(1), Now));
        Assert.Empty(state.Update(MakeSnapshot(1), Now));
        List<AlertEvent> events = state.Update(MakeSnapshot(1), Now).ToList();

        Assert.Single(events.OfType<ThreatIncreased>());
    }
}
