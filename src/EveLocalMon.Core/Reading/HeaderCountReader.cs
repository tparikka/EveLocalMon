using System.Text;
using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

/// <summary>Reads the Local header's member count (e.g. "100") using digit templates.</summary>
public static class HeaderCountReader
{
    public static int? Read(Frame frame, PixelRect rect, IReadOnlyDictionary<char, bool[,]> digits, ReaderOptions options)
    {
        if (digits.Count == 0)
        {
            return null;
        }

        int width = rect.Width;
        int height = rect.Height;
        var lit = new bool[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                lit[y, x] = frame.GetPixel(rect.X + x, rect.Y + y).Luma >= options.DigitLumaMin;
            }
        }

        var colHasLit = new bool[width];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (lit[y, x])
                {
                    colHasLit[x] = true;
                    break;
                }
            }
        }

        var spans = new List<(int Start, int End)>();
        int i = 0;
        while (i < width)
        {
            if (!colHasLit[i])
            {
                i++;
                continue;
            }

            int start = i;
            while (i < width && colHasLit[i]) i++;
            spans.Add((start, i));
        }

        if (spans.Count == 0)
        {
            return null;
        }

        int maxTemplateWidth = digits.Values.Max(d => d.GetLength(1));

        var finalSpans = new List<(int Start, int End)>();
        foreach ((int start, int end) in spans)
        {
            int spanWidth = end - start;
            if (maxTemplateWidth > 0 && spanWidth > 1.6 * maxTemplateWidth)
            {
                // Two digits rendered with no dark column between them: cut into template-width cells.
                int cells = Math.Max(1, spanWidth / maxTemplateWidth);
                for (int c = 0; c < cells; c++)
                {
                    int cellStart = start + c * maxTemplateWidth;
                    int cellEnd = c == cells - 1 ? end : cellStart + maxTemplateWidth;
                    finalSpans.Add((cellStart, cellEnd));
                }
            }
            else
            {
                finalSpans.Add((start, end));
            }
        }

        var result = new StringBuilder();
        foreach ((int start, int end) in finalSpans)
        {
            bool[,]? glyph = DigitGlyphs.CropToBoundingBox(lit, start, end, height);
            if (glyph is null)
            {
                return null;
            }

            char? best = null;
            double bestFraction = double.MaxValue;
            foreach ((char ch, bool[,] template) in digits)
            {
                double fraction = DigitGlyphs.MismatchFraction(glyph, template);
                if (fraction < bestFraction)
                {
                    bestFraction = fraction;
                    best = ch;
                }
            }

            if (best is null || bestFraction > options.DigitMaxMismatchFraction)
            {
                return null;
            }

            result.Append(best.Value);
        }

        return int.TryParse(result.ToString(), out int value) ? value : null;
    }
}
