using System.Runtime.InteropServices;
using SkiaSharp;

namespace EveLocalMon.Core.Imaging;

public static class FrameIo
{
    public static Frame LoadPng(string path)
    {
        using SKCodec? codec = SKCodec.Create(path);
        if (codec is null)
        {
            throw new IOException($"Could not decode PNG: {path}");
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new IOException($"Failed to decode PNG '{path}': {result}");
        }

        byte[] pixels = new byte[bitmap.RowBytes * bitmap.Height];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new Frame(bitmap.Width, bitmap.Height, bitmap.RowBytes, pixels);
    }

    public static void SavePng(Frame frame, string path)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        IntPtr dest = bitmap.GetPixels();
        int rowBytes = Math.Min(bitmap.RowBytes, frame.Width * 4);
        for (int y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(frame.Pixels, y * frame.Stride, dest + y * bitmap.RowBytes, rowBytes);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }
}
