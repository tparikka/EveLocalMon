using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Tests.Support;
using Xunit.Abstractions;

namespace EveLocalMon.Core.Tests;

/// <summary>Runs the reader against every real screenshot in Fixtures/*.json and checks every
/// field the fixture declares. On failure, prints the same per-row table as the CLI's `read`
/// command — tuning a threshold means seeing the scores, not just a pass/fail.</summary>
public class FixtureReaderTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> FixtureFiles() =>
        FixtureLoader.AllFixtureFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void MatchesExpectedResults(string fixturePath)
    {
        FixtureDto fixture = FixtureLoader.Load(fixturePath);
        Frame frame = FrameIo.LoadPng(FixtureLoader.Path_(fixture.Image));
        TemplateSet templates = TemplateStore.Load(FixtureLoader.TemplatesDir);
        Calibration calibration = fixture.Calibration.ToCalibration();

        Snapshot snapshot = LocalListReader.Read(frame, calibration, templates);
        ExpectedDto expected = fixture.Expected ?? throw new InvalidOperationException($"{fixturePath} has no 'expected' section.");

        try
        {
            Assert.Equal(expected.OccupiedFullRows, snapshot.OccupiedFullRows);
            Assert.Equal(expected.HasPartialRowWithContent, snapshot.HasPartialRowWithContent);
            Assert.Equal(expected.HeaderMemberCount, snapshot.HeaderMemberCount);
            Assert.Equal(expected.Readability, snapshot.Readability.ToString());
            Assert.Equal(expected.ThreatTotal, snapshot.Threats.Total);

            if (expected.PhaseFirstFullSlotTop is { } expectedTop)
            {
                RowResult firstFull = snapshot.Rows.First(r => !r.IsPartial);
                Assert.Equal(expectedTop, firstFull.SlotTop);
            }

            if (expected.Counts is { } counts)
            {
                List<RowResult> full = snapshot.Rows.Where(r => !r.IsPartial).ToList();
                Assert.Equal(counts.Friendly, full.Count(r => r.Category == RowCategory.Friendly));
                Assert.Equal(counts.Enemy, full.Count(r => r.Category == RowCategory.Enemy));
                Assert.Equal(counts.Neutral, full.Count(r => r.Category == RowCategory.Neutral));
                Assert.Equal(counts.Unknown, full.Count(r => r.Category == RowCategory.Unknown));
                Assert.Equal(counts.NoIcon, full.Count(r => r.Category == RowCategory.NoIcon));
            }

            if (expected.RowCodes is { } codes)
            {
                string actual = string.Concat(snapshot.Rows
                    .Where(r => !r.IsPartial && r.Category != RowCategory.Empty)
                    .Select(RowCode));
                Assert.Equal(codes, actual);
            }
        }
        catch
        {
            PrintDebugTable(snapshot);
            throw;
        }
    }

    private static char RowCode(RowResult row) => row.Category switch
    {
        RowCategory.NoIcon => '-',
        RowCategory.Unknown => '?',
        RowCategory.Neutral => 'N',
        RowCategory.Enemy => 'R',
        RowCategory.Friendly => row.TemplateName switch
        {
            "green-star" => 'G',
            "purple-person" => 'P',
            "blue-star" => 'B',
            "blue-plus" => '+',
            _ => '?',
        },
        _ => '?',
    };

    private void PrintDebugTable(Snapshot snapshot)
    {
        output.WriteLine($"{"Top",5}  {"Kind",-8}  {"Category",-9}  {"Template",-16}  {"Best",6}  {"2ndBest",8}  {"Text",5}  {"Icon",5}");
        foreach (RowResult row in snapshot.Rows)
        {
            string kind = row.IsPartial ? "partial" : "full";
            output.WriteLine($"{row.SlotTop,5}  {kind,-8}  {row.Category,-9}  {row.TemplateName ?? "-",-16}  {row.BestScore,6:F1}  {row.SecondBestScore,8:F1}  {row.TextPixels,5}  {row.IconPixels,5}");
        }

        output.WriteLine($"OccupiedFullRows={snapshot.OccupiedFullRows} HasPartialRowWithContent={snapshot.HasPartialRowWithContent} " +
            $"HeaderMemberCount={snapshot.HeaderMemberCount} Readability={snapshot.Readability} ThreatTotal={snapshot.Threats.Total}");
    }
}
