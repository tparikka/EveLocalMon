using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using EveLocalMon.Core.Imaging;
using CorePixelRect = EveLocalMon.Core.Imaging.PixelRect;

namespace EveLocalMon.App.Views;

/// <summary>
/// A dimmed, click-through-free overlay on every monitor; drag to pick the Local pane.
///
/// The fiddly part is that Avalonia positions windows in physical pixels but sizes them in DIPs,
/// so on a scaled monitor the two must be converted separately or the overlay covers only part of
/// the screen (doc/M2-SPEC.md §3).
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private readonly Screen _screen;
    private readonly TaskCompletionSource<CorePixelRect?> _result;
    private readonly Canvas _canvas = new();
    private readonly Border _selection;
    private readonly TextBlock _readout;
    private (int X, int Y)? _dragStartScreen;

    private RegionPickerWindow(Screen screen, TaskCompletionSource<CorePixelRect?> result)
    {
        _screen = screen;
        _result = result;

        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;

        Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;

        _selection = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x4D, 0xA3, 0xFF)),
            IsVisible = false,
        };

        _readout = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            Padding = new Thickness(6, 3),
            IsVisible = false,
        };

        var hint = new TextBlock
        {
            Text = "Drag around the Local member list  —  Esc to cancel",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        Canvas.SetLeft(hint, Math.Max(0, (Width / 2) - 180));
        Canvas.SetTop(hint, Math.Max(0, (Height / 2) - 20));

        _canvas.Children.Add(hint);
        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_readout);
        Content = _canvas;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
    }

    /// <summary>Shows an overlay on every monitor and resolves with the chosen region in screen
    /// coordinates (physical pixels), or null if cancelled.</summary>
    public static async Task<CorePixelRect?> PickAsync(Window owner)
    {
        var result = new TaskCompletionSource<CorePixelRect?>();
        var windows = owner.Screens.All.Select(screen => new RegionPickerWindow(screen, result)).ToList();

        foreach (RegionPickerWindow window in windows)
        {
            window.Show();
        }

        windows.FirstOrDefault()?.Activate();

        try
        {
            return await result.Task;
        }
        finally
        {
            foreach (RegionPickerWindow window in windows)
            {
                window.Close();
            }
        }
    }

    private (int X, int Y) ToScreen(Point pointInWindow) => (
        CoordinateSpaces.DipToPhysical(_screen.Bounds.X, pointInWindow.X, _screen.Scaling),
        CoordinateSpaces.DipToPhysical(_screen.Bounds.Y, pointInWindow.Y, _screen.Scaling));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _result.TrySetResult(null);
            return;
        }

        _dragStartScreen = ToScreen(point.Position);
        _selection.IsVisible = true;
        _readout.IsVisible = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartScreen is not { } start) return;

        UpdateSelection(start, ToScreen(e.GetPosition(this)));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStartScreen is not { } start) return;

        CorePixelRect region = BuildRegion(start, ToScreen(e.GetPosition(this)));
        _dragStartScreen = null;

        // Too small to be a deliberate selection — treat as a mis-click, not a 3px region.
        if (region.Width < 20 || region.Height < 20)
        {
            _selection.IsVisible = false;
            _readout.IsVisible = false;
            return;
        }

        _result.TrySetResult(region);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result.TrySetResult(null);
        }
    }

    private CorePixelRect BuildRegion((int X, int Y) start, (int X, int Y) end)
    {
        CorePixelRect raw = CoordinateSpaces.FromCorners(start.X, start.Y, end.X, end.Y);
        var bounds = new CorePixelRect(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height);
        return CoordinateSpaces.ClampTo(raw, bounds);
    }

    private void UpdateSelection((int X, int Y) start, (int X, int Y) current)
    {
        CorePixelRect region = BuildRegion(start, current);

        double left = CoordinateSpaces.PhysicalToDip(_screen.Bounds.X, region.X, _screen.Scaling);
        double top = CoordinateSpaces.PhysicalToDip(_screen.Bounds.Y, region.Y, _screen.Scaling);

        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = CoordinateSpaces.PhysicalSizeToDip(region.Width, _screen.Scaling);
        _selection.Height = CoordinateSpaces.PhysicalSizeToDip(region.Height, _screen.Scaling);

        _readout.Text = $"{region.Width} × {region.Height} px  at  ({region.X}, {region.Y})";
        Canvas.SetLeft(_readout, left);
        Canvas.SetTop(_readout, Math.Max(0, top - 26));
    }
}
