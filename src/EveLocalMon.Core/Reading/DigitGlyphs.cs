namespace EveLocalMon.Core.Reading;

/// <summary>Shared helpers for binary (lit/unlit) digit glyph comparison, used by both
/// <see cref="TemplateStore"/> (loading digit templates) and <see cref="HeaderCountReader"/>
/// (reading a candidate glyph from a captured frame).</summary>
internal static class DigitGlyphs
{
    /// <summary>Crops to the bounding box of lit pixels within columns [startX, endX) of a
    /// full-height lit-pixel grid. Returns null if nothing is lit in that column range.</summary>
    public static bool[,]? CropToBoundingBox(bool[,] lit, int startX, int endX, int height)
    {
        int minY = height, maxY = -1, minX = endX, maxX = startX - 1;
        for (int y = 0; y < height; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!lit[y, x]) continue;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        }

        if (maxY < 0)
        {
            return null;
        }

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        var cropped = new bool[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                cropped[y, x] = lit[minY + y, minX + x];
            }
        }

        return cropped;
    }

    /// <summary>Fraction of mismatched cells over the union bounding box of the two grids —
    /// tolerant of a size difference of a pixel or two without needing an explicit alignment search.</summary>
    public static double MismatchFraction(bool[,] a, bool[,] b)
    {
        int h = Math.Max(a.GetLength(0), b.GetLength(0));
        int w = Math.Max(a.GetLength(1), b.GetLength(1));
        int mismatches = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool av = y < a.GetLength(0) && x < a.GetLength(1) && a[y, x];
                bool bv = y < b.GetLength(0) && x < b.GetLength(1) && b[y, x];
                if (av != bv) mismatches++;
            }
        }

        return (double)mismatches / (h * w);
    }
}
