using System.Diagnostics;

namespace EveLocalMon.Core.Imaging;

/// <summary>
/// A captured or loaded image: BGRA8888, top-down (row 0 is the top row), <see cref="Stride"/>
/// bytes per row. Bytes within a pixel are ordered B, G, R, A. Alpha is never read by
/// detection code — it exists only so template PNGs can carry a compare mask.
/// </summary>
public sealed class Frame
{
    public Frame(int width, int height, int stride, byte[] pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride), "Stride must fit at least width*4 bytes.");
        if (pixels.Length < stride * (height - 1) + width * 4) throw new ArgumentException("Pixel buffer is smaller than width/height/stride imply.", nameof(pixels));

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public Bgr GetPixel(int x, int y)
    {
        AssertInBounds(x, y);
        int offset = y * Stride + x * 4;
        return new Bgr(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]);
    }

    [Conditional("DEBUG")]
    private void AssertInBounds(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside a {Width}x{Height} frame.");
        }
    }

    /// <summary>Copies the given rectangle into a new, tightly-packed (stride == width*4) frame.</summary>
    public Frame Crop(PixelRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Right > Width || rect.Bottom > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), $"{rect} does not fit inside a {Width}x{Height} frame.");
        }

        int destStride = rect.Width * 4;
        byte[] dest = new byte[destStride * rect.Height];
        for (int row = 0; row < rect.Height; row++)
        {
            int srcOffset = (rect.Y + row) * Stride + rect.X * 4;
            int destOffset = row * destStride;
            Array.Copy(Pixels, srcOffset, dest, destOffset, destStride);
        }

        return new Frame(rect.Width, rect.Height, destStride, dest);
    }
}
