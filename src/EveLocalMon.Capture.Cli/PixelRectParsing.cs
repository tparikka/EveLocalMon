using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Capture.Cli;

internal static class PixelRectParsing
{
    /// <summary>Parses "x,y,w,h".</summary>
    public static PixelRect Parse(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], out int x)
            || !int.TryParse(parts[1], out int y)
            || !int.TryParse(parts[2], out int w)
            || !int.TryParse(parts[3], out int h))
        {
            throw new FormatException($"Expected \"x,y,w,h\", got \"{text}\".");
        }

        return new PixelRect(x, y, w, h);
    }
}
