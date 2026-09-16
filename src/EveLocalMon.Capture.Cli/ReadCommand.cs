using System.Text.Json;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Capture.Cli;

internal static class ReadCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: read <src.png> <fixture.json> <templatesDir>");
            return 1;
        }

        string imagePath = args[0];
        string fixturePath = args[1];
        string templatesDir = args[2];

        FixtureDto? fixture = JsonSerializer.Deserialize<FixtureDto>(File.ReadAllText(fixturePath), JsonOptions);
        if (fixture is null)
        {
            Console.Error.WriteLine($"Could not parse '{fixturePath}'.");
            return 1;
        }

        Frame frame = FrameIo.LoadPng(imagePath);
        Calibration calibration = fixture.Calibration.ToCalibration();
        TemplateSet templates = TemplateStore.Load(templatesDir);

        Snapshot snapshot = LocalListReader.Read(frame, calibration, templates);

        Console.WriteLine($"{"Top",5}  {"Kind",-8}  {"Category",-9}  {"Template",-16}  {"Best",6}  {"2ndBest",8}  {"Text",5}  {"Icon",5}");
        foreach (RowResult row in snapshot.Rows)
        {
            string kind = row.IsPartial ? "partial" : "full";
            string best = double.IsFinite(row.BestScore) && row.BestScore < double.MaxValue ? row.BestScore.ToString("F1") : "-";
            string secondBest = double.IsFinite(row.SecondBestScore) && row.SecondBestScore < double.MaxValue ? row.SecondBestScore.ToString("F1") : "-";
            Console.WriteLine($"{row.SlotTop,5}  {kind,-8}  {row.Category,-9}  {row.TemplateName ?? "-",-16}  {best,6}  {secondBest,8}  {row.TextPixels,5}  {row.IconPixels,5}");
        }

        Console.WriteLine();
        Console.WriteLine($"OccupiedFullRows: {snapshot.OccupiedFullRows}");
        Console.WriteLine($"HasPartialRowWithContent: {snapshot.HasPartialRowWithContent}");
        Console.WriteLine($"HeaderMemberCount: {snapshot.HeaderMemberCount?.ToString() ?? "null"}");
        Console.WriteLine($"Readability: {snapshot.Readability}");
        Console.WriteLine($"Threats: total={snapshot.Threats.Total} enemy={snapshot.Threats.Enemy} neutral={snapshot.Threats.Neutral} unknown={snapshot.Threats.Unknown} extraNoIcon={snapshot.Threats.ExtraNoIcon}");

        if (fixture.Expected is { } expected)
        {
            Console.WriteLine();
            Console.WriteLine("Expected vs. actual:");
            Report("OccupiedFullRows", expected.OccupiedFullRows, snapshot.OccupiedFullRows);
            Report("HasPartialRowWithContent", expected.HasPartialRowWithContent, snapshot.HasPartialRowWithContent);
            Report("HeaderMemberCount", expected.HeaderMemberCount, snapshot.HeaderMemberCount);
            Report("Readability", expected.Readability, snapshot.Readability.ToString());
            Report("ThreatTotal", expected.ThreatTotal, snapshot.Threats.Total);
        }

        return 0;
    }

    private static void Report<T>(string label, T expected, T actual)
    {
        bool pass = EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL"),-4} {label}: expected={expected} actual={actual}");
    }
}
