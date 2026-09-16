using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;

namespace EveLocalMon.Core.Tests;

/// <summary>Modifies a loaded sample in memory to exercise scenarios the two real screenshots
/// don't happen to show — see doc/M1-SPEC.md §5 "Synthetic reader tests".</summary>
public class SyntheticReaderTests
{
    // Matches Fixtures/sample-local-nonscrolled-one-neut-one-friendly.json's calibration.
    private static readonly Calibration Sample2Calibration = new(
        new PixelRect(310, 72, 132, 546), IconX: 314, NameX: 329, NameRight: 440,
        HeaderCountRect: new PixelRect(330, 53, 30, 16));

    // Matches Fixtures/sample-local-mixed-overflow.json's calibration.
    private static readonly Calibration Sample1Calibration = new(
        new PixelRect(300, 68, 116, 967), IconX: 303, NameX: 318, NameRight: 414,
        HeaderCountRect: new PixelRect(320, 44, 30, 16));

    private static Frame LoadSample2() => FrameIo.LoadPng(FixtureLoader.Path_("sample-local-nonscrolled-one-neut-one-friendly.png"));

    private static Frame LoadSample1() => FrameIo.LoadPng(FixtureLoader.Path_("sample-local-mixed-overflow.png"));

    private static TemplateSet LoadTemplates() => TemplateStore.Load(FixtureLoader.TemplatesDir);

    [Fact]
    public void UnrecognisedIconBecomesUnknownAndCountsAsAThreat()
    {
        Frame frame = LoadSample2();
        TemplateSet templates = LoadTemplates();

        // Row 2 (top=95) is normally blue-plus (Friendly). Repaint it solid orange, keeping the
        // corners (excluded from matching either way) so it still looks like a plausible icon.
        FramePoking.FillRect(frame, x0: 314, y0: 95, w: 12, h: 12, r: 255, g: 140, b: 0, skip: FramePoking.IconCorners(12));

        Snapshot snapshot = LocalListReader.Read(frame, Sample2Calibration, templates);

        RowResult row2 = snapshot.Rows.Where(r => !r.IsPartial).ElementAt(1);
        Assert.Equal(RowCategory.Unknown, row2.Category);
        Assert.Equal(2, snapshot.Threats.Total); // was 1 (the gray neutral); +1 unknown
    }

    [Fact]
    public void IconPaintedOverWithBackgroundBecomesNoIconAndCountsBeyondTheOwnPilotAllowance()
    {
        Frame frame = LoadSample2();
        TemplateSet templates = LoadTemplates();

        // Erase the blue-plus icon entirely; the name text next to it is untouched, so the row
        // is still occupied — now a second no-icon pilot besides the active client's own row.
        FramePoking.FillRect(frame, x0: 314, y0: 95, w: 12, h: 12, r: 19, g: 23, b: 26);

        Snapshot snapshot = LocalListReader.Read(frame, Sample2Calibration, templates);

        RowResult row2 = snapshot.Rows.Where(r => !r.IsPartial).ElementAt(1);
        Assert.Equal(RowCategory.NoIcon, row2.Category);
        Assert.Equal(1, snapshot.Threats.ExtraNoIcon); // allowance of 1 already used by the own pilot's row
        Assert.Equal(2, snapshot.Threats.Total); // 1 gray neutral + 1 extra no-icon
    }

    [Fact]
    public void HostileIconOnTheOwnPilotsRowIsStillDetected()
    {
        Frame frame = LoadSample2();
        TemplateSet templates = LoadTemplates();
        IconTemplate redMinus = templates.Icons.Single(t => t.Name == "red-minus");

        // Row 1 (top=78) is the active client's own pilot, normally NoIcon. Paste a hostile icon
        // there — the reader must not special-case row 1 away from being a real threat.
        FramePoking.PasteTemplate(frame, x0: 314, y0: 78, redMinus);

        Snapshot snapshot = LocalListReader.Read(frame, Sample2Calibration, templates);

        RowResult row1 = snapshot.Rows.Where(r => !r.IsPartial).ElementAt(0);
        Assert.Equal(RowCategory.Enemy, row1.Category);
        Assert.Equal(2, snapshot.Threats.Total); // 1 enemy + 1 gray neutral (own-pilot no-icon row is gone)
    }

    [Fact]
    public void EmptyListMeansNotVisibleNotAnEmptySystem()
    {
        Frame frame = LoadSample2();
        TemplateSet templates = LoadTemplates();
        PixelRect list = Sample2Calibration.ListRect;

        FramePoking.FillRect(frame, list.X, list.Y, list.Width, list.Height, r: 19, g: 23, b: 26);

        Snapshot snapshot = LocalListReader.Read(frame, Sample2Calibration, templates);

        Assert.Equal(0, snapshot.OccupiedFullRows);
        Assert.Equal(Readability.NotVisible, snapshot.Readability);
        Assert.Equal(0, snapshot.Threats.Total);
    }

    [Fact]
    public void HeaderCountAloneStillSignalsOverflowWhenRowsAlignExactlyWithTheViewport()
    {
        Frame frame = LoadSample1();
        TemplateSet templates = LoadTemplates();

        // 56 rows at pitch 17 starting exactly at the first slot top, ending just past the last:
        // no partial row exists, so only the header-count-vs-visible-rows signal can catch this.
        var calibration = Sample1Calibration with { ListRect = new PixelRect(300, 81, 116, 952) };

        Snapshot snapshot = LocalListReader.Read(frame, calibration, templates);

        Assert.False(snapshot.HasPartialRowWithContent);
        Assert.Equal(56, snapshot.OccupiedFullRows);
        Assert.Equal(100, snapshot.HeaderMemberCount);
        Assert.Equal(Readability.Overflow, snapshot.Readability);
    }

    [Fact]
    public void WithoutAHeaderCountRectRowAlignedOverflowGoesUndetected()
    {
        Frame frame = LoadSample1();
        TemplateSet templates = LoadTemplates();

        var calibration = Sample1Calibration with
        {
            ListRect = new PixelRect(300, 81, 116, 952),
            HeaderCountRect = null,
        };

        Snapshot snapshot = LocalListReader.Read(frame, calibration, templates);

        Assert.False(snapshot.HasPartialRowWithContent);
        Assert.Null(snapshot.HeaderMemberCount);
        // Documents the gap: with rows aligned to the viewport and no header count configured,
        // overflow has no signal left to detect it.
        Assert.Equal(Readability.Full, snapshot.Readability);
    }
}
