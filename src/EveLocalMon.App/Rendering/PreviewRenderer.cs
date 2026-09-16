using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.App.Rendering;

/// <summary>
/// Draws the captured region plus per-row classification into a <see cref="WriteableBitmap"/>.
/// Overlays are composited straight into the pixels rather than laid out as controls, so what you
/// see is exactly the pixels the reader saw, with no layout or scaling in between.
/// </summary>
public static class PreviewRenderer
{
    private static readonly (byte B, byte G, byte R) Friendly = (90, 200, 90);
    private static readonly (byte B, byte G, byte R) Enemy = (60, 60, 240);
    private static readonly (byte B, byte G, byte R) Neutral = (40, 190, 250);
    private static readonly (byte B, byte G, byte R) Unknown = (230, 80, 230);
    private static readonly (byte B, byte G, byte R) NoIcon = (190, 190, 190);
    private static readonly (byte B, byte G, byte R) Guide = (110, 110, 110);

    public static WriteableBitmap CreateBitmap(int width, int height) =>
        new(new PixelSize(Math.Max(1, width), Math.Max(1, height)), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    public static void Render(WriteableBitmap target, Frame frame, Snapshot? snapshot, Calibration? calibration)
    {
        byte[] buffer = (byte[])frame.Pixels.Clone();
        int stride = frame.Stride;

        if (calibration is not null)
        {
            // var, not PixelRect: Avalonia defines its own PixelRect and both usings are in scope.
            var list = calibration.ListRect;
            DrawRect(buffer, stride, frame, list.X, list.Y, list.Width, list.Height, Guide, dashed: true);
            DrawVertical(buffer, stride, frame, calibration.IconX, list.Y, list.Height, Guide);
            DrawVertical(buffer, stride, frame, calibration.IconX + calibration.IconSize - 1, list.Y, list.Height, Guide);
            DrawVertical(buffer, stride, frame, calibration.NameX, list.Y, list.Height, Guide);

            if (calibration.HeaderCountRect is { } header)
            {
                DrawRect(buffer, stride, frame, header.X, header.Y, header.Width, header.Height, Guide, dashed: false);
            }
        }

        if (snapshot is not null && calibration is not null)
        {
            int left = Math.Max(0, calibration.IconX - 1);
            int right = Math.Min(frame.Width, calibration.NameRight);
            foreach (RowResult row in snapshot.Rows)
            {
                (byte B, byte G, byte R)? colour = row.Category switch
                {
                    RowCategory.Friendly => Friendly,
                    RowCategory.Enemy => Enemy,
                    RowCategory.Neutral => Neutral,
                    RowCategory.Unknown => Unknown,
                    RowCategory.NoIcon => NoIcon,
                    _ => row.IsPartial ? Guide : null,
                };

                if (colour is null) continue;

                DrawRect(buffer, stride, frame, left, row.SlotTop, right - left, calibration.IconSize, colour.Value, row.IsPartial);
            }
        }

        using ILockedFramebuffer fb = target.Lock();
        int rowBytes = Math.Min(fb.RowBytes, frame.Width * 4);
        int rows = Math.Min(frame.Height, fb.Size.Height);
        for (int y = 0; y < rows; y++)
        {
            Marshal.Copy(buffer, y * stride, fb.Address + (y * fb.RowBytes), rowBytes);
        }
    }

    private static void DrawRect(byte[] buffer, int stride, Frame frame, int x, int y, int width, int height, (byte B, byte G, byte R) colour, bool dashed)
    {
        int right = x + width - 1;
        int bottom = y + height - 1;
        for (int px = x; px <= right; px++)
        {
            if (dashed && (px & 1) == 1) continue;
            SetPixel(buffer, stride, frame, px, y, colour);
            SetPixel(buffer, stride, frame, px, bottom, colour);
        }

        for (int py = y; py <= bottom; py++)
        {
            if (dashed && (py & 1) == 1) continue;
            SetPixel(buffer, stride, frame, x, py, colour);
            SetPixel(buffer, stride, frame, right, py, colour);
        }
    }

    private static void DrawVertical(byte[] buffer, int stride, Frame frame, int x, int y, int height, (byte B, byte G, byte R) colour)
    {
        for (int py = y; py < y + height; py += 3)
        {
            SetPixel(buffer, stride, frame, x, py, colour);
        }
    }

    private static void SetPixel(byte[] buffer, int stride, Frame frame, int x, int y, (byte B, byte G, byte R) colour)
    {
        if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height) return;

        int offset = y * stride + x * 4;
        buffer[offset] = colour.B;
        buffer[offset + 1] = colour.G;
        buffer[offset + 2] = colour.R;
    }
}
