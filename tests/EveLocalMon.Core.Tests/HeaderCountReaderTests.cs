using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;

namespace EveLocalMon.Core.Tests;

public class HeaderCountReaderTests
{
    private static readonly ReaderOptions Options = new();

    [Fact]
    public void ReadsOneHundredFromSample1()
    {
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_("sample-local-mixed-overflow.png"));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        int? count = HeaderCountReader.Read(frame, new PixelRect(320, 44, 30, 16), templates.Digits, Options);

        Assert.Equal(100, count);
    }

    [Fact]
    public void ReadsThreeFromSample2()
    {
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_("sample-local-nonscrolled-one-neut-one-friendly.png"));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        int? count = HeaderCountReader.Read(frame, new PixelRect(330, 53, 30, 16), templates.Digits, Options);

        Assert.Equal(3, count);
    }

    [Fact]
    public void UnrecognisedGlyphShapeReturnsNull()
    {
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        // A single solid lit block: nothing like the hollow strokes of any digit glyph, so it
        // should mismatch every template and the whole read should come back null.
        const int width = 20, height = 16;
        var lit = new bool[height, width];
        for (int y = 3; y < 13; y++)
        {
            for (int x = 5; x < 15; x++)
            {
                lit[y, x] = true;
            }
        }

        Frame frame = BuildFrame(width, height, lit);

        int? count = HeaderCountReader.Read(frame, new PixelRect(0, 0, width, height), templates.Digits, Options);

        Assert.Null(count);
    }

    private static Frame BuildFrame(int width, int height, bool[,] lit)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = lit[y, x] ? (byte)200 : (byte)10;
                int offset = y * stride + x * 4;
                pixels[offset] = v;
                pixels[offset + 1] = v;
                pixels[offset + 2] = v;
                pixels[offset + 3] = 255;
            }
        }

        return new Frame(width, height, stride, pixels);
    }
}
