using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Audio;
using EveLocalMon.Core.Net;
using EveLocalMon.Platform.Windows;

namespace EveLocalMon.Capture.Cli;

/// <summary>
/// Fires an alert channel without opening the GUI. The popup channel is GUI-only, so this covers
/// sound and ntfy — the two that can fail silently for reasons outside the app (no audio device,
/// wrong topic, no network).
/// </summary>
internal static class TestAlertCommand
{
    public static int Run(string[] args)
    {
        string channel = "all";
        string server = "https://ntfy.sh";
        string? topic = null;
        int volume = 70;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--channel":
                    channel = args[++i];
                    break;
                case "--server":
                    server = args[++i];
                    break;
                case "--topic":
                    topic = args[++i];
                    break;
                case "--volume":
                    volume = int.Parse(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        var message = new AlertMessage(
            AlertKind.ThreatIncreased,
            "EVE: hostile in Local",
            "1 red — 1 threat visible (test alert)",
            AlertPriority.Urgent,
            DateTimeOffset.Now);

        bool ok = true;

        if (channel is "sound" or "all")
        {
            try
            {
                using var player = new WinmmSoundPlayer();
                player.Play(AlertTone.BuildWav(volume));
                Console.WriteLine($"Sound: played the built-in chime at {volume}% volume.");
                // PlaySound is async; give it time to finish before the process exits.
                Thread.Sleep(600);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Sound: FAILED — {ex.Message}");
                ok = false;
            }
        }

        if (channel is "ntfy" or "all")
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                Console.Error.WriteLine("ntfy: skipped — pass --topic (anyone who knows an ntfy.sh topic can read it, so use a long random one).");
                ok = channel != "ntfy" && ok;
            }
            else
            {
                using var http = new HttpClient();
                var client = new NtfyClient(http);
                bool sent = client.SendAsync(message, new NtfyOptions(server, topic)).GetAwaiter().GetResult();
                Console.WriteLine(sent ? $"ntfy: sent to {server.TrimEnd('/')}/{topic}" : "ntfy: FAILED — see the server and topic.");
                ok &= sent;
            }
        }

        if (channel is not ("sound" or "ntfy" or "all"))
        {
            Console.Error.WriteLine($"Unknown channel '{channel}'. Use sound, ntfy or all.");
            return 1;
        }

        return ok ? 0 : 1;
    }
}
