using Avalonia.Threading;
using EveLocalMon.Core.Capture;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Platform.Windows;

namespace EveLocalMon.App.Services;

public sealed record MonitorUpdate(Frame Frame, Snapshot? Snapshot, string? Error);

/// <summary>
/// Captures the configured region on a background task and runs the reader over it, publishing
/// results on the UI thread. Unchanged frames skip the reader entirely — at 2 Hz over a ~140x900
/// region this keeps the app effectively free, which matters for something meant to sit running
/// behind a game.
/// </summary>
public sealed class MonitorLoop : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<MonitorUpdate>? Updated;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start(PixelRect region, Calibration? calibration, TemplateSet templates, int intervalMs)
    {
        Stop();

        var cts = new CancellationTokenSource();
        _cts = cts;
        _task = Task.Run(() => RunAsync(region, calibration, templates, intervalMs, cts.Token), cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task RunAsync(PixelRect region, Calibration? calibration, TemplateSet templates, int intervalMs, CancellationToken token)
    {
        using var capture = new GdiScreenCapture();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, intervalMs)));
        byte[]? previous = null;

        while (!token.IsCancellationRequested)
        {
            try
            {
                Frame frame = capture.Capture(region);

                bool unchanged = previous is not null && frame.Pixels.AsSpan().SequenceEqual(previous);
                if (!unchanged)
                {
                    previous = frame.Pixels;
                    Snapshot? snapshot = calibration is null ? null : LocalListReader.Read(frame, calibration, templates);
                    Publish(new MonitorUpdate(frame, snapshot, null));
                }
            }
            catch (Exception ex)
            {
                Publish(new MonitorUpdate(EmptyFrame(), null, ex.Message));
            }

            if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void Publish(MonitorUpdate update) => Dispatcher.UIThread.Post(() => Updated?.Invoke(update));

    private static Frame EmptyFrame() => new(1, 1, 4, new byte[4]);

    public void Dispose() => Stop();
}
