using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.App.Views;

/// <summary>Channel switches, ntfy details, test buttons and mute.</summary>
public sealed class AlertsPanel : StackPanel
{
    private readonly (CheckBox Sound, CheckBox Popup, CheckBox Ntfy) _threats;
    private readonly (CheckBox Sound, CheckBox Popup, CheckBox Ntfy) _unreadable;
    private readonly (CheckBox Sound, CheckBox Popup, CheckBox Ntfy) _allClear;
    private readonly NumericUpDown _volume = new() { Minimum = 0, Maximum = 100, Increment = 5, FormatString = "0", Width = 96 };
    private readonly TextBox _ntfyServer = new() { PlaceholderText = "https://ntfy.sh" };
    private readonly TextBox _ntfyTopic = new() { PlaceholderText = "generate one →" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11 };

    public AlertsPanel()
    {
        Spacing = 4;

        Children.Add(new TextBlock { Text = "Alerts", FontWeight = FontWeight.SemiBold });
        _threats = AddChannelRow("Threats");
        _unreadable = AddChannelRow("Unreadable");
        _allClear = AddChannelRow("All clear");

        Children.Add(Row(new TextBlock { Text = "Volume", Width = 80, VerticalAlignment = VerticalAlignment.Center }, _volume));
        Children.Add(new TextBlock { Text = "ntfy server", FontSize = 11 });
        Children.Add(_ntfyServer);
        Children.Add(new TextBlock { Text = "ntfy topic (anyone who knows it can read your alerts)", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        Children.Add(_ntfyTopic);

        var generate = new Button { Content = "Generate topic" };
        generate.Click += (_, _) => _ntfyTopic.Text = AlertDispatcher.GenerateTopic();

        var apply = new Button { Content = "Apply" };
        apply.Click += (_, _) => Changed?.Invoke(Value);

        Children.Add(Row(generate, apply));

        var testSound = new Button { Content = "Test sound" };
        testSound.Click += (_, _) => TestRequested?.Invoke("sound");
        var testPopup = new Button { Content = "Test popup" };
        testPopup.Click += (_, _) => TestRequested?.Invoke("popup");
        var testNtfy = new Button { Content = "Test ntfy" };
        testNtfy.Click += (_, _) => TestRequested?.Invoke("ntfy");
        Children.Add(Row(testSound, testPopup, testNtfy));

        var mute = new Button { Content = "Mute 15 min" };
        mute.Click += (_, _) => MuteRequested?.Invoke(TimeSpan.FromMinutes(15));
        var unmute = new Button { Content = "Unmute" };
        unmute.Click += (_, _) => MuteRequested?.Invoke(null);
        Children.Add(Row(mute, unmute));

        Children.Add(_status);
    }

    public event Action<AlertSettings>? Changed;
    public event Action<string>? TestRequested;
    public event Action<TimeSpan?>? MuteRequested;

    public AlertSettings Value
    {
        get => new()
        {
            Threats = Read(_threats),
            Unreadable = Read(_unreadable),
            AllClear = Read(_allClear),
            VolumePercent = (int)(_volume.Value ?? 70),
            NtfyServer = string.IsNullOrWhiteSpace(_ntfyServer.Text) ? "https://ntfy.sh" : _ntfyServer.Text!.Trim(),
            NtfyTopic = string.IsNullOrWhiteSpace(_ntfyTopic.Text) ? null : _ntfyTopic.Text!.Trim(),
        };

        set
        {
            Write(_threats, value.Threats);
            Write(_unreadable, value.Unreadable);
            Write(_allClear, value.AllClear);
            _volume.Value = value.VolumePercent;
            _ntfyServer.Text = value.NtfyServer;
            _ntfyTopic.Text = value.NtfyTopic ?? string.Empty;
        }
    }

    public void SetStatus(string text) => _status.Text = text;

    private (CheckBox Sound, CheckBox Popup, CheckBox Ntfy) AddChannelRow(string label)
    {
        var sound = new CheckBox { Content = "sound" };
        var popup = new CheckBox { Content = "popup" };
        var ntfy = new CheckBox { Content = "ntfy" };

        Children.Add(new TextBlock { Text = label, FontSize = 11 });
        Children.Add(Row(sound, popup, ntfy));
        return (sound, popup, ntfy);
    }

    private static AlertChannelSet Read((CheckBox Sound, CheckBox Popup, CheckBox Ntfy) row) =>
        new(row.Sound.IsChecked ?? false, row.Popup.IsChecked ?? false, row.Ntfy.IsChecked ?? false);

    private static void Write((CheckBox Sound, CheckBox Popup, CheckBox Ntfy) row, AlertChannelSet set)
    {
        row.Sound.IsChecked = set.Sound;
        row.Popup.IsChecked = set.Popup;
        row.Ntfy.IsChecked = set.Ntfy;
    }

    private static StackPanel Row(params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }
}
