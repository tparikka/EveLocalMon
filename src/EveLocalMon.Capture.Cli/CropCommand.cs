using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Capture.Cli;

internal static class CropCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: crop <src.png> x,y,w,h <out.png>");
            return 1;
        }

        Frame source = FrameIo.LoadPng(args[0]);
        PixelRect rect = PixelRectParsing.Parse(args[1]);
        Frame cropped = source.Crop(rect);
        FrameIo.SavePng(cropped, args[2]);
        Console.WriteLine($"Wrote {rect.Width}x{rect.Height} crop to {args[2]}");
        return 0;
    }
}
