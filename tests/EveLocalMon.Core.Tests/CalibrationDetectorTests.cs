using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;
using Xunit.Abstractions;

namespace EveLocalMon.Core.Tests;

/// <summary>
/// Feeds the detector the kind of region a user would drag around the Local pane, cut out of the
/// real screenshots. The second test is the one that matters: a calibration is only correct if the
/// reader, given it, reproduces the fixture's known-good results.
/// </summary>
public class CalibrationDetectorTests(ITestOutputHelper output)
{
    // A plausible drag around each sample's member pane, in that screenshot's coordinates.
    private static readonly PixelRect Sample1Region = new(298, 40, 138, 995);
    private static readonly PixelRect Sample2Region = new(306, 44, 137, 574);

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
    [InlineData("sample-local-mixed-overflow.png", 56, true, 100, "Overflow", 2)]
    [InlineData("sample-local-nonscrolled-one-neut-one-friendly.png", 3, false, 3, "Full", 1)]
    public void AutoDetectedCalibrationReproducesFixtureResults(
        string image, int occupiedRows, bool hasPartial, int? headerCount, string readability, int threats)
    {
        PixelRect regionRect = image.Contains("mixed") ? Sample1Region : Sample2Region;
        Frame region = LoadRegion(image, regionRect);
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
