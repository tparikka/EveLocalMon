using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using EveLocalMon.App.Views;
using EveLocalMon.Core.Alerts;
using CorePixelRect = EveLocalMon.Core.Imaging.PixelRect;

namespace EveLocalMon.App.Services;

/// <summary>
/// Shows alert popups near the EVE window being watched, not on whichever monitor Windows thinks
/// is primary — the point is to appear where the user is already looking.
/// </summary>
public sealed class AlertPresenter(Window owner, Func<CorePixelRect?> watchedRegion) : IAlertPresenter
{
    private const int MaxVisible = 3;
    private const int MarginPhysical = 16;

    private readonly List<AlertPopupWindow> _live = [];

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(8);

    public void Present(AlertMessage message) => Dispatcher.UIThread.Post(() => Show(message));

    private void Show(AlertMessage message)
    {
        while (_live.Count >= MaxVisible)
        {
            AlertPopupWindow oldest = _live[0];
            _live.RemoveAt(0);
            oldest.Close();
        }

        Screen screen = FindScreen();
        var popup = new AlertPopupWindow(message, Lifetime);

        int width = (int)Math.Round(AlertPopupWindow.PopupWidth * screen.Scaling);
        int height = (int)Math.Round(AlertPopupWindow.PopupHeight * screen.Scaling);
        int slot = _live.Count;

        popup.Position = new PixelPoint(
            screen.WorkingArea.Right - width - MarginPhysical,
            screen.WorkingArea.Bottom - ((slot + 1) * (height + 8)) - MarginPhysical);

        popup.Closed += (_, _) => _live.Remove(popup);
        _live.Add(popup);
        popup.Show();
    }

    private Screen FindScreen()
    {
        if (watchedRegion() is { } region)
        {
            int centreX = region.X + (region.Width / 2);
            int centreY = region.Y + (region.Height / 2);

            foreach (Screen candidate in owner.Screens.All)
            {
                PixelRect bounds = candidate.Bounds;
                if (centreX >= bounds.X && centreX < bounds.Right && centreY >= bounds.Y && centreY < bounds.Bottom)
                {
                    return candidate;
                }
            }
        }

        return owner.Screens.Primary ?? owner.Screens.All[0];
    }
}
