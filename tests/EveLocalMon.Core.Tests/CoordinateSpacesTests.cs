using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Tests;

/// <summary>A sign or scaling slip here is invisible on one 100% monitor and silently wrong on a
/// second, scaled one — hence the negative-origin and 125%/150% cases.</summary>
public class CoordinateSpacesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2200, 300)]
    [InlineData(-1920, -200)]
    public void ScreenAndRegionRoundTrip(int regionX, int regionY)
    {
        var region = new PixelRect(regionX, regionY, 140, 900);

        (int fx, int fy) = CoordinateSpaces.ScreenToRegion(region, regionX + 17, regionY + 42);
        Assert.Equal((17, 42), (fx, fy));

        (int sx, int sy) = CoordinateSpaces.RegionToScreen(region, fx, fy);
        Assert.Equal((regionX + 17, regionY + 42), (sx, sy));
    }

    [Fact]
    public void RectConversionsKeepSizeAndRoundTrip()
    {
        var region = new PixelRect(-1920, -200, 140, 900);
        var screenRect = new PixelRect(-1900, -160, 12, 12);

        PixelRect inRegion = CoordinateSpaces.ScreenToRegion(region, screenRect);
        Assert.Equal(new PixelRect(20, 40, 12, 12), inRegion);
        Assert.Equal(screenRect, CoordinateSpaces.RegionToScreen(region, inRegion));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void DipAndPhysicalRoundTrip(double scaling)
    {
        const int origin = -1920;
        foreach (int physical in new[] { origin, origin + 1, origin + 100, origin + 999 })
        {
            double dip = CoordinateSpaces.PhysicalToDip(origin, physical, scaling);
            Assert.Equal(physical, CoordinateSpaces.DipToPhysical(origin, dip, scaling));
        }
    }

    [Fact]
    public void PhysicalSizeToDipShrinksByScaling()
    {
        Assert.Equal(800, CoordinateSpaces.PhysicalSizeToDip(1000, 1.25));
    }

    [Fact]
    public void FromCornersAcceptsADragInAnyDirection()
    {
        var expected = new PixelRect(10, 20, 30, 40);
        Assert.Equal(expected, CoordinateSpaces.FromCorners(10, 20, 40, 60));
        Assert.Equal(expected, CoordinateSpaces.FromCorners(40, 60, 10, 20));
        Assert.Equal(expected, CoordinateSpaces.FromCorners(40, 20, 10, 60));
    }

    [Fact]
    public void ClampToConfinesADragToItsMonitor()
    {
        var monitor = new PixelRect(-1920, 0, 1920, 1080);

        Assert.Equal(new PixelRect(-1920, 0, 100, 100), CoordinateSpaces.ClampTo(new PixelRect(-2000, -50, 100, 100), monitor));
        // The monitor's right edge is x=0, so a 100px-wide rect can start no further right than -100.
        Assert.Equal(new PixelRect(-100, 980, 100, 100), CoordinateSpaces.ClampTo(new PixelRect(100, 1500, 100, 100), monitor));
        // A selection bigger than the monitor shrinks to it rather than hanging off the edge.
        Assert.Equal(monitor, CoordinateSpaces.ClampTo(new PixelRect(-3000, -3000, 5000, 5000), monitor));
    }

    [Fact]
    public void IntersectReturnsEmptyWhenDisjoint()
    {
        PixelRect empty = CoordinateSpaces.Intersect(new PixelRect(0, 0, 10, 10), new PixelRect(100, 100, 10, 10));
        Assert.Equal(0, empty.Width);
        Assert.Equal(0, empty.Height);

        Assert.Equal(
            new PixelRect(5, 5, 5, 5),
            CoordinateSpaces.Intersect(new PixelRect(0, 0, 10, 10), new PixelRect(5, 5, 20, 20)));
    }
}
