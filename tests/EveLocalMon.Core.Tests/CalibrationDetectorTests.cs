using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;
using Xunit.Abstractions;

namespace EveLocalMon.Core.Tests;

/// <summary>
/// Feeds the detector the kind of region a user would drag around the Local pane, cut out of the
/// real screenshots. The reader-agreement test is the one that matters: a calibration is only
/// correct if the reader, given it, reproduces the fixture's known-good results.
/// </summary>
public class CalibrationDetectorTests(ITestOutputHelper output)
{
    // A plausible drag around each sample's member pane, in that screenshot's coordinates.
    private static readonly PixelRect Sample1Region = new(298, 40, 138, 995);
    private static readonly PixelRect Sample2Region = new(306, 44, 137, 574);
    private static readonly PixelRect Sample3Region = new(333, 36, 138, 564);

    private static Frame LoadRegion(string image, PixelRect region) =>
        FrameIo.LoadPng(FixtureLoader.Path_(image)).Crop(region);

    [Fact]
    public void DetectsSample1Geometry()
    {
        Frame region = LoadRegion("sample-local-mixed-overflow.png", Sample1Region);

        DetectionResult result = CalibrationDetector.Detect(region);
        foreach (string note in result.Notes) output.WriteLine(note);

        Assert.Equal(DetectionConfidence.Good, result.Confidence);
        Calibration cal = Assert.IsType<Calibration>(result.Calibration);

        // Known-good absolute values from doc/M1-SPEC.md §4.3, shifted into region coordinates.
        Assert.InRange(cal.IconX, 303 - Sample1Region.X - 2, 303 - Sample1Region.X + 2);
        Assert.InRange(cal.NameX, 318 - Sample1Region.X - 2, 318 - Sample1Region.X + 2);
        Assert.Equal(17, cal.RowPitch);
        Assert.Equal(12, cal.IconSize);
        Assert.Equal(68 - Sample1Region.Y, cal.ListRect.Y); // the clipped first row must stay inside
        Assert.NotNull(cal.HeaderCountRect);
    }

    [Fact]
    public void DetectsSample2Geometry()
    {
        Frame region = LoadRegion("sample-local-nonscrolled-one-neut-one-friendly.png", Sample2Region);

        DetectionResult result = CalibrationDetector.Detect(region);
        foreach (string note in result.Notes) output.WriteLine(note);

        Assert.Equal(DetectionConfidence.Good, result.Confidence);
        Calibration cal = Assert.IsType<Calibration>(result.Calibration);

        Assert.InRange(cal.IconX, 314 - Sample2Region.X - 2, 314 - Sample2Region.X + 2);
        Assert.InRange(cal.NameX, 329 - Sample2Region.X - 2, 329 - Sample2Region.X + 2);
        Assert.Equal(17, cal.RowPitch);
        Assert.Equal(12, cal.IconSize);
        // Not scrolled: the first row is whole, so the viewport starts at its slot top.
        Assert.Equal(78 - Sample2Region.Y, cal.ListRect.Y);
        Assert.NotNull(cal.HeaderCountRect);
    }

    [Theory]
    [InlineData("sample-local-mixed-overflow.png", 298, 40, 138, 995, 56, true, 100, "Overflow", 2)]
    [InlineData("sample-local-nonscrolled-one-neut-one-friendly.png", 306, 44, 137, 574, 3, false, 3, "Full", 1)]
    [InlineData("sample-2-hostiles.png", 333, 36, 138, 564, 14, false, 14, "Full", 2)]
    // 44 pilots, all four icon types, and a chat pane full of decoy columns in the same region.
    [InlineData("sample-18fleet-1hostile-15friendly-9corp.png", 0, 0, 475, 994, 44, false, 44, "Full", 1)]
    public void AutoDetectedCalibrationReproducesFixtureResults(
        string image, int rx, int ry, int rw, int rh,
        int occupiedRows, bool hasPartial, int? headerCount, string readability, int threats)
    {
        Frame region = LoadRegion(image, new PixelRect(rx, ry, rw, rh));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        DetectionResult detection = CalibrationDetector.Detect(region);
        foreach (string note in detection.Notes) output.WriteLine(note);
        Calibration cal = Assert.IsType<Calibration>(detection.Calibration);

        Snapshot snapshot = LocalListReader.Read(region, cal, templates);

        output.WriteLine($"occupied={snapshot.OccupiedFullRows} partial={snapshot.HasPartialRowWithContent} " +
            $"header={snapshot.HeaderMemberCount} readability={snapshot.Readability} threats={snapshot.Threats.Total}");

        Assert.Equal(occupiedRows, snapshot.OccupiedFullRows);
        Assert.Equal(hasPartial, snapshot.HasPartialRowWithContent);
        Assert.Equal(headerCount, snapshot.HeaderMemberCount);
        Assert.Equal(readability, snapshot.Readability.ToString());
        Assert.Equal(threats, snapshot.Threats.Total);
    }

    [Fact]
    public void OrangeStandingIconsAreReadAsHostileNotUnknown()
    {
        Frame region = LoadRegion("sample-2-hostiles.png", Sample3Region);
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        DetectionResult detection = CalibrationDetector.Detect(region);
        Calibration cal = Assert.IsType<Calibration>(detection.Calibration);
        Snapshot snapshot = LocalListReader.Read(region, cal, templates);

        List<RowResult> full = snapshot.Rows.Where(r => !r.IsPartial).ToList();
        Assert.Equal(2, full.Count(r => r.Category == RowCategory.Enemy));
        Assert.Equal(11, full.Count(r => r.Category == RowCategory.Friendly));
        Assert.Equal(1, full.Count(r => r.Category == RowCategory.NoIcon));
        Assert.Equal(0, full.Count(r => r.Category == RowCategory.Unknown));
        Assert.Equal(0, snapshot.Threats.ExtraNoIcon); // the own pilot's icon-less row
        Assert.Equal(2, snapshot.Threats.Total);
    }

    /// <summary>
    /// The bug this guards: a region dragged wide enough to include the chat log offers coloured
    /// chat text as a false "icon column". Taking one turned chat lines into phantom pilots and
    /// reported 5 hostiles where there were 2.
    ///
    /// Two outcomes are acceptable — turn the region down, or see past the decoys to the real
    /// column — so this asserts the thing that actually matters: the counts are never inflated.
    /// </summary>
    [Fact]
    public void AChatLogInsideTheRegionNeverProducesPhantomThreats()
    {
        Frame whole = FrameIo.LoadPng(FixtureLoader.Path_("sample-2-hostiles.png"));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);

        DetectionResult result = CalibrationDetector.Detect(whole);
        foreach (string note in result.Notes) output.WriteLine(note);

        // Every chat-text decoy must be explicitly turned down, with a reason.
        Assert.Contains(result.Notes, n => n.StartsWith("Ignored a possible icon column", StringComparison.Ordinal));

        if (result.Calibration is not { } calibration)
        {
            Assert.Equal(DetectionConfidence.Failed, result.Confidence);
            return;
        }

        // Having settled on a column, it must be the real one, and the counts must be the truth:
        // 2 hostiles, 11 fleet, 1 icon-less own pilot — not the 5 that were reported.
        Snapshot snapshot = LocalListReader.Read(whole, calibration, templates);
        Assert.Equal(339, calibration.IconX);
        Assert.Equal(17, calibration.RowPitch);
        Assert.Equal(14, snapshot.OccupiedFullRows);
        Assert.Equal(2, snapshot.Threats.Enemy);
        Assert.Equal(0, snapshot.Threats.ExtraNoIcon);
        Assert.Equal(2, snapshot.Threats.Total);
    }

    [Fact]
    public void ABlankRegionFailsRatherThanInventingACalibration()
    {
        var pixels = new byte[140 * 4 * 900];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 19;
            pixels[i + 1] = 23;
            pixels[i + 2] = 26;
            pixels[i + 3] = 255;
        }

        DetectionResult result = CalibrationDetector.Detect(new Frame(140, 900, 140 * 4, pixels));

        Assert.Equal(DetectionConfidence.Failed, result.Confidence);
        Assert.Null(result.Calibration);
        Assert.NotEmpty(result.Notes);
    }
}
