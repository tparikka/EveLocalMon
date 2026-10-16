using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

public class AlertDispatcherTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 20, 0, 0, TimeSpan.Zero);

    private static ThreatIncreased Threat(int previous = 0, int current = 1) =>
        new(previous, current, new ThreatBreakdown(current, 0, 0, 0), Start);

    private sealed class FakeSound : ISoundPlayer
    {
        public int Plays { get; private set; }
        public Exception? ThrowOnPlay { get; set; }

        public void Play(byte[] wav)
        {
            if (ThrowOnPlay is not null) throw ThrowOnPlay;
            Plays++;
        }
    }

    private sealed class FakePresenter : IAlertPresenter
    {
        public List<AlertMessage> Shown { get; } = [];

        public void Present(AlertMessage message) => Shown.Add(message);
    }

    private sealed class FakeNtfy : INtfyClient
    {
        public List<AlertMessage> Sent { get; } = [];
        public bool Result { get; set; } = true;

        public Task<bool> SendAsync(AlertMessage message, NtfyOptions options, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(Result);
        }
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AlertDispatcher Dispatcher, FakeSound Sound, FakePresenter Popup, FakeNtfy Ntfy, TestTime Time) Build(
        AlertSettings? settings = null)
    {
        var sound = new FakeSound();
        var popup = new FakePresenter();
        var ntfy = new FakeNtfy();
        var time = new TestTime();
        var dispatcher = new AlertDispatcher(sound, popup, ntfy, time)
        {
            Settings = settings ?? new AlertSettings { NtfyTopic = "test-topic" },
        };

        return (dispatcher, sound, popup, ntfy, time);
    }

    [Fact]
    public async Task ThreatsReachEveryEnabledChannel()
    {
        var (dispatcher, sound, popup, ntfy, _) = Build();

        await dispatcher.DispatchAsync([Threat()]);

        Assert.Equal(1, sound.Plays);
        Assert.Single(popup.Shown);
        Assert.Single(ntfy.Sent);
    }

    [Fact]
    public async Task AllClearIsPopupOnlyByDefault()
    {
        var (dispatcher, sound, popup, ntfy, _) = Build();

        await dispatcher.DispatchAsync([new LocalClear(Start)]);

        Assert.Equal(0, sound.Plays);
        Assert.Single(popup.Shown);
        Assert.Empty(ntfy.Sent);
    }

    [Fact]
    public async Task UnreadableUsesNtfyToo()
    {
        var (dispatcher, sound, popup, ntfy, _) = Build();

        await dispatcher.DispatchAsync([new ReadabilityChanged(Readability.Full, Readability.Overflow, 100, 56, Start)]);

        Assert.Equal(1, sound.Plays);
        Assert.Single(popup.Shown);
        Assert.Single(ntfy.Sent);
    }

    [Fact]
    public async Task RateLimitingIsPerChannelSoABurstStillUpdatesThePopup()
    {
        var (dispatcher, sound, popup, _, time) = Build();

        await dispatcher.DispatchAsync([Threat(0, 1)]);
        time.Now = Start.AddMilliseconds(1200); // past the popup's 1s bar, inside the sound's 3s one
        await dispatcher.DispatchAsync([Threat(1, 2)]);

        Assert.Equal(1, sound.Plays);
        Assert.Equal(2, popup.Shown.Count);
    }

    [Fact]
    public async Task SuppressedAlertsAreDroppedNotQueued()
    {
        var (dispatcher, sound, _, _, time) = Build();

        await dispatcher.DispatchAsync([Threat(0, 1)]);
        time.Now = Start.AddMilliseconds(500);
        await dispatcher.DispatchAsync([Threat(1, 2)]);

        // Letting time pass must not release a backlog: a stale count is worse than none.
        time.Now = Start.AddMinutes(5);
        Assert.Equal(1, sound.Plays);
    }

    [Fact]
    public async Task MuteSilencesSoundAndPushButNeverThePopup()
    {
        var (dispatcher, sound, popup, ntfy, time) = Build();
        dispatcher.MuteFor(TimeSpan.FromMinutes(5));

        await dispatcher.DispatchAsync([Threat()]);

        Assert.Equal(0, sound.Plays);
        Assert.Empty(ntfy.Sent);
        Assert.Single(popup.Shown);

        time.Now = Start.AddMinutes(6);
        await dispatcher.DispatchAsync([Threat(1, 2)]);

        Assert.Equal(1, sound.Plays);
        Assert.Single(ntfy.Sent);
    }

    [Fact]
    public async Task AFailingChannelDoesNotStopTheOthers()
    {
        var (dispatcher, sound, popup, ntfy, _) = Build();
        sound.ThrowOnPlay = new InvalidOperationException("no audio device");
        var failures = new List<string>();
        dispatcher.ChannelFailed += failures.Add;

        await dispatcher.DispatchAsync([Threat()]);

        Assert.Single(popup.Shown);
        Assert.Single(ntfy.Sent);
        Assert.Contains(failures, f => f.StartsWith("sound:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedPushIsReported()
    {
        var (dispatcher, _, _, ntfy, _) = Build();
        ntfy.Result = false;
        var failures = new List<string>();
        dispatcher.ChannelFailed += failures.Add;

        await dispatcher.DispatchAsync([Threat()]);

        Assert.Contains(failures, f => f.StartsWith("ntfy:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NtfyWithoutATopicReportsRatherThanSendingNowhere()
    {
        var (dispatcher, _, _, ntfy, _) = Build(new AlertSettings { NtfyTopic = null });
        var failures = new List<string>();
        dispatcher.ChannelFailed += failures.Add;

        await dispatcher.DispatchAsync([Threat()]);

        Assert.Empty(ntfy.Sent);
        Assert.Contains(failures, f => f.Contains("no topic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GeneratedTopicsAreLongAndUnguessable()
    {
        string first = AlertDispatcher.GenerateTopic();
        string second = AlertDispatcher.GenerateTopic();

        Assert.StartsWith("evelocalmon-", first, StringComparison.Ordinal);
        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, second);
    }
}
