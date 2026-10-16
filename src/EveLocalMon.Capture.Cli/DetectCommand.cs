using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Capture.Cli;

/// <summary>
/// Runs auto-detect and then the reader over a screenshot, printing everything both decided.
/// This is the tool for "the app says N hostiles but I can see M": point it at the screenshot the
/// user sent and the per-row table shows which rows were counted and how close each icon came to
/// a known template.
/// </summary>
internal static class DetectCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: detect <src.png> [--region x,y,w,h] [--templates <dir>]");
            return 1;
        }

        string imagePath = args[0];
        PixelRect? region = null;
        string templatesDir = Path.Combine(AppContext.BaseDirectory, "assets", "templates");

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--region":
                    region = PixelRectParsing.Parse(args[++i]);
                    break;
                case "--templates":
                    templatesDir = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        Frame frame = FrameIo.LoadPng(imagePath);
        if (region is { } rect)
        {
            frame = frame.Crop(rect);
            Console.WriteLine($"Cropped to {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}).");
        }

        TemplateSet templates = TemplateStore.Load(templatesDir);
        Console.WriteLine($"Templates: {string.Join(", ", templates.Icons.Select(t => $"{t.Name}[{t.Category}]"))}");

        DetectionResult detection = CalibrationDetector.Detect(frame);
        Console.WriteLine($"Detection: {detection.Confidence}");
        foreach (string note in detection.Notes)
        {
            Console.WriteLine($"  {note}");
        }

        if (detection.Calibration is not { } calibration)
        {
            return 1;
        }

        Console.WriteLine($"Calibration: listRect={calibration.ListRect.X},{calibration.ListRect.Y},{calibration.ListRect.Width},{calibration.ListRect.Height} " +
            $"iconX={calibration.IconX} iconSize={calibration.IconSize} nameX={calibration.NameX} nameRight={calibration.NameRight} " +
            $"pitch={calibration.RowPitch} header={calibration.HeaderCountRect?.ToString() ?? "none"}");

        Snapshot snapshot = LocalListReader.Read(frame, calibration, templates);

        Console.WriteLine();
        Console.WriteLine($"{"Top",5}  {"Kind",-8}  {"Category",-9}  {"Closest",-16}  {"Best",6}  {"2ndBest",8}  {"Text",5}  {"Icon",5}");
        foreach (RowResult row in snapshot.Rows)
        {
            string best = row.BestScore >= double.MaxValue ? "-" : row.BestScore.ToString("F1");
            string second = row.SecondBestScore >= double.MaxValue ? "-" : row.SecondBestScore.ToString("F1");
            Console.WriteLine($"{row.SlotTop,5}  {(row.IsPartial ? "partial" : "full"),-8}  {row.Category,-9}  " +
                $"{row.TemplateName ?? "-",-16}  {best,6}  {second,8}  {row.TextPixels,5}  {row.IconPixels,5}");
        }

        Console.WriteLine();
        Console.WriteLine($"OccupiedFullRows: {snapshot.OccupiedFullRows}");
        Console.WriteLine($"HeaderMemberCount: {snapshot.HeaderMemberCount?.ToString() ?? "null"}");
        Console.WriteLine($"Readability: {snapshot.Readability}");
        Console.WriteLine($"Threats: total={snapshot.Threats.Total} enemy={snapshot.Threats.Enemy} neutral={snapshot.Threats.Neutral} " +
            $"unknown={snapshot.Threats.Unknown} extraNoIcon={snapshot.Threats.ExtraNoIcon}");
        return 0;
    }
}
