using System.Diagnostics;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;

namespace EveLocalMon.Core.Tests;

/// <summary>Excluded from the standard test run — see CLAUDE.md — because timing assertions are
/// noisy on shared/CI hardware. Run explicitly with: dotnet test --filter Category=Perf -c Release</summary>
public class PerformanceTests
{
    [Fact]
    [Trait("Category", "Perf")]
    public void ReadAveragesUnderFiveMillisecondsOnSample1()
    {
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_("sample-local-mixed-overflow.png"));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);
        var calibration = new Calibration(
            new PixelRect(300, 68, 116, 967), IconX: 303, NameX: 318, NameRight: 414,
            HeaderCountRect: new PixelRect(320, 44, 30, 16));

        for (int i = 0; i < 10; i++)
        {
            LocalListReader.Read(frame, calibration, templates);
        }

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            LocalListReader.Read(frame, calibration, templates);
        }

        stopwatch.Stop();

        double averageMs = stopwatch.Elapsed.TotalMilliseconds / 100.0;
        Assert.True(averageMs < 5.0, $"Average Read() time was {averageMs:F2}ms, expected < 5ms (Release build).");
    }
}
