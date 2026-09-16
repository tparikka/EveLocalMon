using System.Diagnostics;
using EveLocalMon.Core.Capture;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Platform.Windows;

namespace EveLocalMon.Capture.Cli;

internal static class CaptureCommand
{
    public static int Run(string[] args)
    {
        PixelRect? region = null;
        string? outDir = null;
        double intervalSeconds = 2;
        int? count = null;
        bool dedupe = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--region":
                    region = PixelRectParsing.Parse(args[++i]);
                    break;
                case "--out":
                    outDir = args[++i];
                    break;
                case "--interval":
                    intervalSeconds = double.Parse(args[++i]);
                    break;
                case "--count":
                    count = int.Parse(args[++i]);
                    break;
                case "--no-dedupe":
                    dedupe = false;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        if (region is null || outDir is null)
        {
            Console.Error.WriteLine("Usage: capture --region x,y,w,h --out <dir> [--interval <sec>=2] [--count <saved frames>] [--no-dedupe]");
            return 1;
        }

        if (count is <= 0)
        {
            Console.Error.WriteLine("--count must be at least 1.");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        using var cancelSource = new CancelKeyPressSource();
        using var screenCapture = new GdiScreenCapture();
        var session = new CaptureSession(new CaptureSessionOptions { MaxSavedFrames = count, Dedupe = dedupe });
        var stopwatch = Stopwatch.StartNew();

        string countText = count is { } max ? $"{max} frame(s)" : "until Ctrl+C";
        Console.WriteLine($"Capturing {region.Value.Width}x{region.Value.Height} at ({region.Value.X},{region.Value.Y}) " +
            $"every {intervalSeconds}s to '{outDir}', {countText}." + (dedupe ? " Identical frames are skipped." : ""));

        string stopReason = "Stopped";
        while (!cancelSource.Token.IsCancellationRequested)
        {
            Frame frame = screenCapture.Capture(region.Value);
            CaptureOutcome outcome = session.Offer(frame);

            if (outcome.Decision == CaptureDecision.Save)
            {
                string fileName = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
                FrameIo.SavePng(frame, Path.Combine(outDir, fileName));
                Console.WriteLine(outcome.SkippedBefore > 0
                    ? $"Saved {fileName} ({outcome.SkippedBefore} identical frame(s) skipped)"
                    : $"Saved {fileName}");
            }

            if (session.IsComplete)
            {
                stopReason = $"Reached --count {count}";
                break;
            }

            try
            {
                Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancelSource.Token).Wait(cancelSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (cancelSource.Token.IsCancellationRequested)
        {
            stopReason = "Ctrl+C";
        }

        if (session.PendingSkipped > 0)
        {
            Console.WriteLine($"({session.PendingSkipped} identical frame(s) skipped at the end)");
        }

        Console.WriteLine($"{stopReason}, stopping.");
        Console.WriteLine($"{session.FramesCaptured} frames captured, {session.FramesSaved} saved, " +
            $"{session.FramesSkipped} identical skipped, {stopwatch.Elapsed.TotalSeconds:F1} s elapsed.");
        return 0;
    }

    private sealed class CancelKeyPressSource : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConsoleCancelEventHandler _handler;

        public CancelKeyPressSource()
        {
            _handler = (_, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
            };
            Console.CancelKeyPress += _handler;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            Console.CancelKeyPress -= _handler;
            _cts.Dispose();
        }
    }
}
