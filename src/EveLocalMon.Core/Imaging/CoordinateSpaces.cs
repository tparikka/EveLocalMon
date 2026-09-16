namespace EveLocalMon.Core.Imaging;

/// <summary>
/// Conversions between the three coordinate spaces this app juggles (doc/M2-SPEC.md §2):
/// <list type="bullet">
/// <item><b>Screen</b> — physical pixels on the virtual desktop, possibly negative.</item>
/// <item><b>Region/frame</b> — origin at the captured region's top-left. All
/// <see cref="Reading.Calibration"/> values live here, so a saved calibration survives the EVE
/// window moving: re-pick the region and the inner offsets still hold.</item>
/// <item><b>DIP</b> — Avalonia layout units; physical pixels divided by the monitor's scaling.</item>
/// </list>
/// Kept in one place, and unit tested, because a sign or scaling error here is invisible on a
/// single 100% monitor and silently wrong on a second, scaled one.
/// </summary>
public static class CoordinateSpaces
{
    public static (int X, int Y) ScreenToRegion(PixelRect region, int screenX, int screenY) =>
        (screenX - region.X, screenY - region.Y);

    public static (int X, int Y) RegionToScreen(PixelRect region, int frameX, int frameY) =>
        (region.X + frameX, region.Y + frameY);

    public static PixelRect ScreenToRegion(PixelRect region, PixelRect screenRect) =>
        new(screenRect.X - region.X, screenRect.Y - region.Y, screenRect.Width, screenRect.Height);

    public static PixelRect RegionToScreen(PixelRect region, PixelRect frameRect) =>
        new(region.X + frameRect.X, region.Y + frameRect.Y, frameRect.Width, frameRect.Height);

    /// <summary>DIPs inside a window whose top-left sits at <paramref name="originPhysical"/>.</summary>
    public static int DipToPhysical(int originPhysical, double dip, double scaling) =>
        originPhysical + (int)Math.Round(dip * scaling, MidpointRounding.AwayFromZero);

    public static double PhysicalToDip(int originPhysical, int physical, double scaling) =>
        (physical - originPhysical) / scaling;

    /// <summary>Size in DIPs of a physical-pixel extent on a monitor with the given scaling.</summary>
    public static double PhysicalSizeToDip(int physical, double scaling) => physical / scaling;

    /// <summary>Builds a rectangle from two drag corners in any order.</summary>
    public static PixelRect FromCorners(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int x = Math.Max(a.X, b.X);
        int y = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= x || bottom <= y ? new PixelRect(x, y, 0, 0) : new PixelRect(x, y, right - x, bottom - y);
    }

    /// <summary>Clamps a rectangle so it lies inside <paramref name="bounds"/>, keeping its size
    /// where possible. Used to confine a drag to the monitor it started on.</summary>
    public static PixelRect ClampTo(PixelRect rect, PixelRect bounds)
    {
        int width = Math.Min(rect.Width, bounds.Width);
        int height = Math.Min(rect.Height, bounds.Height);
        int x = Math.Clamp(rect.X, bounds.X, Math.Max(bounds.X, bounds.Right - width));
        int y = Math.Clamp(rect.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height));
        return new PixelRect(x, y, width, height);
    }
}
