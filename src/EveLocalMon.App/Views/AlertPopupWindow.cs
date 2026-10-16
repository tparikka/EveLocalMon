using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.App.Views;

/// <summary>
/// The app's own notification, deliberately not a Windows toast: Focus Assist / Do Not Disturb
/// routinely suppresses toast banners during fullscreen games, which is exactly when this alert
/// matters. <see cref="Window.ShowActivated"/> is false so it never takes focus off the game.
/// </summary>
public sealed class AlertPopupWindow : Window
{
    public const double PopupWidth = 360;
    public const double PopupHeight = 104;

    private readonly DispatcherTimer _autoDismiss;

    public AlertPopupWindow(AlertMessage message, TimeSpan lifetime)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = PopupWidth;
        Height = PopupHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        Color accent = message.Priority switch
        {
            AlertPriority.Urgent => Color.FromRgb(0xD9, 0x3A, 0x3A),
            AlertPriority.High => Color.FromRgb(0xE0, 0x93, 0x1E),
            _ => Color.FromRgb(0x6B, 0x7A, 0x86),
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x21)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = message.Title,
                        FontWeight = FontWeight.Bold,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(accent),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = message.Body,
                        FontSize = 12,
                        Foreground = Brushes.WhiteSmoke,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 2,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = message.At.ToLocalTime().ToString("HH:mm:ss"),
                        FontSize = 10,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    },
                },
            },
        };

        PointerPressed += (_, _) => Close();

        _autoDismiss = new DispatcherTimer { Interval = lifetime };
        _autoDismiss.Tick += (_, _) => Close();
        _autoDismiss.Start();

        Closed += (_, _) => _autoDismiss.Stop();
    }
}
