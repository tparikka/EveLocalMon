using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests.Support;

/// <summary>Direct pixel mutation for synthetic reader tests — pokes a loaded <see cref="Frame"/>
/// in place so a real screenshot can be turned into a hypothetical scenario without needing a
/// new sample image for every case.</summary>
public static class FramePoking
{
    public static void FillRect(Frame frame, int x0, int y0, int w, int h, byte r, byte g, byte b, ISet<(int Dx, int Dy)>? skip = null)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                if (skip is not null && skip.Contains((dx, dy))) continue;
                int offset = (y0 + dy) * frame.Stride + (x0 + dx) * 4;
                frame.Pixels[offset] = b;
                frame.Pixels[offset + 1] = g;
                frame.Pixels[offset + 2] = r;
            }
        }
    }

    public static void PasteTemplate(Frame frame, int x0, int y0, IconTemplate template)
    {
        int size = template.Image.Width;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Bgr px = template.Image.GetPixel(x, y);
                int offset = (y0 + y) * frame.Stride + (x0 + x) * 4;
                frame.Pixels[offset] = px.B;
                frame.Pixels[offset + 1] = px.G;
                frame.Pixels[offset + 2] = px.R;
            }
        }
    }

    /// <summary>The four corner cells of a size*size icon slot, in the (dx,dy) form <see cref="FillRect"/>'s
    /// skip set expects — matching <c>IconMasking.Default</c>'s excluded pixels.</summary>
    public static HashSet<(int Dx, int Dy)> IconCorners(int size) =>
    [
        (0, 0),
        (size - 1, 0),
        (0, size - 1),
        (size - 1, size - 1),
    ];
}
