using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EveLocalMon.App.Rendering;
using EveLocalMon.App.Services;
using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Audio;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Net;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Settings;
using EveLocalMon.Platform.Windows;

namespace EveLocalMon.App.Views;

public sealed class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly MonitorLoop _loop = new();
    private readonly Image _preview = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
    private readonly TextBlock _rowTable = new() { FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 11 };
    private readonly CalibrationPanel _calibrationPanel = new();
    private readonly AlertsPanel _alertsPanel = new();
    private readonly Button _startStop = new() { Content = "Start", Width = 90 };
    private readonly NumericUpDown _interval = new() { Minimum = 100, Maximum = 5000, Increment = 100, FormatString = "0", Width = 90 };

    private readonly HttpClient _httpClient = new();
    private readonly WinmmSoundPlayer _soundPlayer = new();
    private readonly AlertPresenter _presenter;
    private readonly AlertDispatcher _dispatcher;
    private AlertState _alertState = new(new AlertStateOptions());

    private AppSettings _settings;
    private TemplateSet _templates;
    private WriteableBitmap? _bitmap;

    public MainWindow()
    {
        Title = "EveLocalMon — region, preview & alerts";
        Width = 1020;
        Height = 820;

        _settings = _settingsStore.Load(out string? warning);
        string templatesDirectory = _settings.TemplatesDirectory ?? TemplateSeeder.EnsureSeeded();
        _templates = LoadTemplates(templatesDirectory);
        _interval.Value = _settings.PollIntervalMs;

        _presenter = new AlertPresenter(this, () => _settings.Region);
        _dispatcher = new AlertDispatcher(_soundPlayer, _presenter, new NtfyClient(_httpClient))
        {
            Settings = _settings.Alerts,
        };
        _dispatcher.ChannelFailed += message => Dispatcher.UIThread.Post(() => _alertsPanel.SetStatus(message));

        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.None);
        _calibrationPanel.Value = _settings.Calibration;
        _calibrationPanel.Applied += OnCalibrationApplied;

        _alertsPanel.Value = _settings.Alerts;
        _alertsPanel.Changed += OnAlertSettingsChanged;
        _alertsPanel.TestRequested += which => _ = TestChannelAsync(which);
        _alertsPanel.MuteRequested += OnMuteRequested;

        Content = BuildLayout();

        _loop.Updated += OnMonitorUpdate;
        Closing += (_, _) => Shutdown();

        SetStatus(warning is not null
            ? warning
            : _settings.Region is null
                ? "No region selected yet — click \"Select region…\" and drag around EVE's Local member list."
                : $"Region {Describe(_settings.Region.Value)} loaded. Press Start to watch it.");
    }

    private Control BuildLayout()
    {
        var selectRegion = new Button { Content = "Select region…" };
        selectRegion.Click += async (_, _) => await SelectRegionAsync();

        _startStop.Click += (_, _) => ToggleMonitoring();

        var redetect = new Button { Content = "Re-detect calibration" };
        redetect.Click += (_, _) => RedetectCalibration();

        var save = new Button { Content = "Save settings" };
        save.Click += (_, _) => SaveSettings();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(8),
            Children =
            {
                selectRegion,
                _startStop,
                redetect,
                save,
                new TextBlock { Text = "Interval (ms)", VerticalAlignment = VerticalAlignment.Center },
                _interval,
            },
        };

        var side = new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(8),
            Width = 330,
            Children =
            {
                _status,
                _notes,
                _alertsPanel,
                _calibrationPanel,
                new TextBlock { Text = "Rows", FontWeight = FontWeight.SemiBold },
                _rowTable,
            },
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);

        var sideScroll = new ScrollViewer { Content = side, Width = 350 };
        DockPanel.SetDock(sideScroll, Dock.Right);
        layout.Children.Add(sideScroll);

        layout.Children.Add(new ScrollViewer
        {
            Content = _preview,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Avalonia.Thickness(8),
        });

        return layout;
    }

    private async Task SelectRegionAsync()
    {
        bool wasRunning = _loop.IsRunning;
        _loop.Stop();
        _startStop.Content = "Start";

        // Get out of our own screenshot before the overlay goes up.
        WindowState = WindowState.Minimized;
        await Task.Delay(200);

        PixelRect? picked = await RegionPickerWindow.PickAsync(this);

        WindowState = WindowState.Normal;
        Activate();

        if (picked is not { } region || region.Width < 20 || region.Height < 20)
        {
            SetStatus("Region selection cancelled; nothing changed.");
            return;
        }

        _settings = _settings with { Region = region };
        RedetectCalibration();
        SaveSettings();

        if (wasRunning)
        {
            ToggleMonitoring();
        }
    }

    private void RedetectCalibration()
    {
        if (_settings.Region is not { } region)
        {
            SetStatus("Select a region first.");
            return;
        }

        try
        {
            Frame frame = CaptureOnce(region);
            DetectionResult detection = CalibrationDetector.Detect(frame);

            _notes.Text = string.Join(Environment.NewLine, detection.Notes);

            if (detection.Calibration is null)
            {
                SetStatus($"⚠ Auto-detect failed. {detection.Notes.FirstOrDefault()} " +
                    "Re-drag the region around just the member list, or set the numbers by hand.");
                RenderPreview(frame, null, null);
                return;
            }

            _settings = _settings with { Calibration = detection.Calibration };
            _calibrationPanel.Value = detection.Calibration;

            Snapshot snapshot = LocalListReader.Read(frame, detection.Calibration, _templates);
            RenderPreview(frame, snapshot, detection.Calibration);

            // A too-wide region is the classic mis-drag: it catches the chat log, whose coloured
            // text can masquerade as an icon column. Say so before the counts get trusted.
            string widthWarning = region.Width > 400
                ? $" ⚠ That region is {region.Width}px wide — a Local member list is usually ~150px. " +
                  "If the rows below look wrong, re-drag around just the member list."
                : string.Empty;

            string confidenceWarning = detection.Confidence == DetectionConfidence.Good
                ? string.Empty
                : $" ⚠ Confidence {detection.Confidence} — check the numbers before trusting the counts.";

            SetStatus($"Detected calibration for region {Describe(region)}.{confidenceWarning}{widthWarning}", snapshot);
        }
        catch (Exception ex)
        {
            SetStatus($"Capture failed: {ex.Message}");
        }
    }

    private void OnCalibrationApplied(Calibration calibration)
    {
        _settings = _settings with { Calibration = calibration };

        if (_loop.IsRunning)
        {
            ToggleMonitoring();
            ToggleMonitoring();
            return;
        }

        if (_settings.Region is { } region)
        {
            try
            {
                Frame frame = CaptureOnce(region);
                Snapshot snapshot = LocalListReader.Read(frame, calibration, _templates);
                RenderPreview(frame, snapshot, calibration);
                SetStatus("Calibration applied.", snapshot);
            }
            catch (Exception ex)
            {
                SetStatus($"Capture failed: {ex.Message}");
            }
        }
    }

    private void OnAlertSettingsChanged(AlertSettings alerts)
    {
        _settings = _settings with { Alerts = alerts };
        _dispatcher.Settings = alerts;
        _settingsStore.Save(_settings);
        _alertsPanel.SetStatus("Alert settings applied and saved.");
    }

    private void OnMuteRequested(TimeSpan? duration)
    {
        if (duration is { } window)
        {
            _dispatcher.MuteFor(window);
            _alertsPanel.SetStatus($"Muted until {DateTimeOffset.Now.Add(window):HH:mm:ss} — popups still appear.");
        }
        else
        {
            _dispatcher.Unmute();
            _alertsPanel.SetStatus("Unmuted.");
        }
    }

    private async Task TestChannelAsync(string channel)
    {
        var message = new AlertMessage(
            AlertKind.ThreatIncreased,
            "EVE: hostile in Local",
            "1 red — 1 threat visible (test alert)",
            AlertPriority.Urgent,
            DateTimeOffset.Now);

        AlertSettings alerts = _alertsPanel.Value;

        try
        {
            switch (channel)
            {
                case "sound":
                    _soundPlayer.Play(AlertTone.BuildWav(alerts.VolumePercent));
                    _alertsPanel.SetStatus("Sound: played the built-in chime.");
                    break;
                case "popup":
                    _presenter.Present(message);
                    _alertsPanel.SetStatus("Popup: shown near the watched region.");
                    break;
                case "ntfy":
                    if (string.IsNullOrWhiteSpace(alerts.NtfyTopic))
                    {
                        _alertsPanel.SetStatus("ntfy: set a topic first (use Generate topic).");
                        return;
                    }

                    bool sent = await new NtfyClient(_httpClient)
                        .SendAsync(message, new NtfyOptions(alerts.NtfyServer, alerts.NtfyTopic));
                    _alertsPanel.SetStatus(sent
                        ? $"ntfy: sent to {alerts.NtfyServer.TrimEnd('/')}/{alerts.NtfyTopic}"
                        : "ntfy: failed — check the server and topic.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _alertsPanel.SetStatus($"{channel}: {ex.Message}");
        }
    }

    private void ToggleMonitoring()
    {
        if (_loop.IsRunning)
        {
            _loop.Stop();
            _startStop.Content = "Start";
            return;
        }

        if (_settings.Region is not { } region)
        {
            SetStatus("Select a region first.");
            return;
        }

        int interval = (int)(_interval.Value ?? 500);
        _settings = _settings with { PollIntervalMs = interval };

        // Fresh alert state per run: stale debounce history from a previous session must not fire
        // an alert the moment monitoring resumes.
        _alertState = new AlertState(new AlertStateOptions());

        _loop.Start(region, _settings.Calibration, _templates, interval);
        _startStop.Content = "Stop";
    }

    private void OnMonitorUpdate(MonitorUpdate update)
    {
        if (update.Error is not null)
        {
            SetStatus($"Capture failed: {update.Error}");
            return;
        }

        RenderPreview(update.Frame, update.Snapshot, _settings.Calibration);
        SetStatus(_settings.Calibration is null
            ? "Watching (no calibration yet — press \"Re-detect calibration\")."
            : "Watching.", update.Snapshot);

        if (update.Snapshot is { } snapshot)
        {
            IReadOnlyList<AlertEvent> events = _alertState.Update(snapshot, DateTimeOffset.Now);
            if (events.Count > 0)
            {
                _ = DispatchAsync(events);
            }
        }
    }

    private async Task DispatchAsync(IReadOnlyList<AlertEvent> events)
    {
        try
        {
            await _dispatcher.DispatchAsync(events);
            AlertMessage last = AlertMessageFactory.Create(events[^1]);
            _alertsPanel.SetStatus($"{last.At.ToLocalTime():HH:mm:ss}  {last.Title} — {last.Body}");
        }
        catch (Exception ex)
        {
            _alertsPanel.SetStatus($"Alert dispatch failed: {ex.Message}");
        }
    }

    private void RenderPreview(Frame frame, Snapshot? snapshot, Calibration? calibration)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap = PreviewRenderer.CreateBitmap(frame.Width, frame.Height);
            _preview.Source = _bitmap;
        }

        PreviewRenderer.Render(_bitmap, frame, snapshot, calibration);

        // Show physical pixels 1:1 whatever the monitor scaling, so the preview matches what the
        // reader sees rather than a resampled approximation of it.
        _preview.Width = frame.Width / RenderScaling;
        _preview.Height = frame.Height / RenderScaling;
        _preview.InvalidateVisual();

        _rowTable.Text = snapshot is null ? string.Empty : BuildRowTable(snapshot);
    }

    private void SetStatus(string message, Snapshot? snapshot = null)
    {
        if (snapshot is null)
        {
            _status.Text = message;
            return;
        }

        string readability = snapshot.Readability switch
        {
            Readability.NotVisible => "NOT VISIBLE — Local window covered, minimised or mis-calibrated. This is not 'all clear'.",
            Readability.Overflow => $"OVERFLOW — more pilots than fit; {snapshot.HeaderMemberCount?.ToString() ?? "?"} in Local, {snapshot.OccupiedFullRows} readable.",
            _ => $"Full — all {snapshot.OccupiedFullRows} rows readable.",
        };

        ThreatBreakdown threats = snapshot.Threats;
        _status.Text = $"""
            {message}

            {readability}
            Threats: {threats.Total}  (enemy {threats.Enemy}, neutral {threats.Neutral}, unknown {threats.Unknown}, extra no-icon {threats.ExtraNoIcon})
            Header count: {snapshot.HeaderMemberCount?.ToString() ?? "unreadable"}
            """;
    }

    private static string BuildRowTable(Snapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{"y",5} {"kind",-7} {"category",-9} {"template",-14} {"best",5}");
        foreach (RowResult row in snapshot.Rows)
        {
            if (row.Category == RowCategory.Empty && !row.IsPartial) continue;

            builder.AppendLine($"{row.SlotTop,5} {(row.IsPartial ? "partial" : "full"),-7} {row.Category,-9} " +
                $"{row.TemplateName ?? "-",-14} {row.BestScore,5:F1}");
        }

        return builder.ToString();
    }

    private void SaveSettings()
    {
        _settings = _settings with
        {
            PollIntervalMs = (int)(_interval.Value ?? 500),
            Alerts = _alertsPanel.Value,
        };
        _dispatcher.Settings = _settings.Alerts;
        _settingsStore.Save(_settings);
        SetStatus($"Settings saved to {_settingsStore.FilePath}");
    }

    private void Shutdown()
    {
        _loop.Dispose();
        _soundPlayer.Dispose();
        _httpClient.Dispose();
    }

    private static Frame CaptureOnce(PixelRect region)
    {
        using var capture = new GdiScreenCapture();
        return capture.Capture(region);
    }

    private static TemplateSet LoadTemplates(string directory)
    {
        try
        {
            return TemplateStore.Load(directory);
        }
        catch (Exception)
        {
            return new TemplateSet([], new Dictionary<char, bool[,]>());
        }
    }

    private static string Describe(PixelRect region) => $"{region.Width}×{region.Height} at ({region.X},{region.Y})";
}
