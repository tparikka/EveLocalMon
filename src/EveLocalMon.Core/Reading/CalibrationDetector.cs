using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

public enum DetectionConfidence
{
    /// <summary>Nothing list-shaped was found; <see cref="DetectionResult.Calibration"/> is null.</summary>
    Failed,

    /// <summary>A calibration was produced, but a fallback was used somewhere — check it before trusting it.</summary>
    Partial,

    /// <summary>Every step found what it expected.</summary>
    Good,
}

public sealed record DetectionResult(
    Calibration? Calibration,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Notes);

public sealed record DetectorOptions
{
    public int TextLumaMin { get; init; } = 120;
    public int IconPixelLumaMin { get; init; } = 70;
    public int IconPixelChromaMin { get; init; } = 40;
    public int MinIconSize { get; init; } = 8;
    public int MaxIconSize { get; init; } = 20;
    public int MinRowPitch { get; init; } = 10;
    public int MaxRowPitch { get; init; } = 40;

    /// <summary>How many rows to walk upward looking for the list's top edge.</summary>
    public int MaxUpwardWalk { get; init; } = 6;

    /// <summary>A column lit on at least this fraction of all rows is a border/divider line,
    /// not name text — text always has gaps between rows.</summary>
    public double LineColumnFraction { get; init; } = 0.8;

    /// <summary>Rows may be at most this much further apart than the icons are tall. EVE's compact
    /// list is 17px for a 12px icon, i.e. a 5px gap.</summary>
    public int MaxRowGap { get; init; } = 8;

    /// <summary>A real icon is a solid block of colour. Measured on doc/samples: genuine icon runs
    /// fill 0.96 of their box (each icon 1.00), while coloured chat text mistaken for an icon
    /// column fills only 0.40 (runs 0.28-0.43). 0.6 separates them with room on both sides.</summary>
    public double MinIconFillFraction { get; init; } = 0.6;

    /// <summary>Icons are square, so the column's width and the runs' height must roughly agree.</summary>
    public int MaxIconAspectDrift { get; init; } = 3;
}

/// <summary>
/// Works out a <see cref="Calibration"/> from one captured region, so the user only has to drag a
/// rectangle around the Local pane. Everything it returns is in region coordinates and is meant to
/// be shown and overridable, never silently trusted — see doc/M2-SPEC.md §4.
///
/// Candidate icon columns are validated rather than assumed: a region that also caught the chat
/// log will offer coloured chat text as a "column", and accepting it silently turns chat lines
/// into phantom pilots (see doc/M3-SPEC.md — this shipped as a bug once already).
/// </summary>
public static class CalibrationDetector
{
    public static DetectionResult Detect(Frame region, DetectorOptions? options = null)
    {
        DetectorOptions opt = options ?? new DetectorOptions();
        var notes = new List<string>();
        int width = region.Width;
        int height = region.Height;

        bool IsText(Bgr p) => p.Luma >= opt.TextLumaMin;
        bool IsIconLike(Bgr p) => p.Luma >= opt.IconPixelLumaMin || p.Chroma >= opt.IconPixelChromaMin;

        var saturatedPerColumn = new int[width];
        var iconLikePerColumn = new int[width];
        var textPerColumn = new int[width];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Bgr p = region.GetPixel(x, y);
                if (p.Chroma >= opt.IconPixelChromaMin) saturatedPerColumn[x]++;
                if (IsIconLike(p)) iconLikePerColumn[x]++;
                if (IsText(p)) textPerColumn[x]++;
            }
        }

        // Saturated columns first (colour icons stand out from gray text); brightness-based
        // candidates are the fallback for a Local showing only gray/neutral icons.
        var candidates = new List<(int Start, int Length, bool Fallback)>();
        candidates.AddRange(FindColumnRuns(saturatedPerColumn, opt).Select(r => (r.Start, r.Length, false)));
        candidates.AddRange(FindColumnRuns(iconLikePerColumn, opt).Select(r => (r.Start, r.Length, true)));

        ColumnAnalysis? chosen = null;
        foreach ((int start, int length, bool fallback) in candidates)
        {
            ColumnAnalysis analysis = AnalyseColumn(region, start, length, opt, IsIconLike);
            if (analysis.Rejection is { } reason)
            {
                notes.Add($"Ignored a possible icon column at x={start}: {reason}");
                continue;
            }

            chosen = analysis with { UsedColumnFallback = fallback };
            break;
        }

        if (chosen is null)
        {
            notes.Add("No icon column that looks like a member list. Drag the region around just the " +
                "Local member list — including the chat log will offer its coloured text as a false match.");
            return new DetectionResult(null, DetectionConfidence.Failed, notes);
        }

        if (chosen.UsedColumnFallback)
        {
            notes.Add("No coloured icons found; icon column located from brightness instead — verify it.");
        }

        if (chosen.UsedPitchFallback)
        {
            notes.Add($"Only one icon visible; assumed a {chosen.RowPitch}px row pitch. Check it against a fuller Local.");
        }

        int iconX = chosen.IconX;
        int iconSize = chosen.IconSize;
        int rowPitch = chosen.RowPitch;

        // --- Name area. Slot-based counting keeps the header digits (above the list) and stray
        // bright pixels from being mistaken for name text.
        int firstSlotTop = chosen.ListIconRuns[0].Start;
        List<int> slotTops = BuildSlotTops(firstSlotTop, rowPitch, iconSize, height);
        int nameSearchStart = Math.Min(width - 1, iconX + chosen.IconColumnWidth);

        var lineColumn = new bool[width];
        for (int x = 0; x < width; x++)
        {
            lineColumn[x] = textPerColumn[x] >= opt.LineColumnFraction * height;
        }

        var slotHits = new int[width];
        int occupiedSlots = 0;
        foreach (int top in slotTops)
        {
            bool occupied = false;
            for (int x = nameSearchStart; x < width; x++)
            {
                if (lineColumn[x]) continue;
                for (int y = top; y < Math.Min(height, top + iconSize); y++)
                {
                    if (!IsText(region.GetPixel(x, y))) continue;
                    slotHits[x]++;
                    occupied = true;
                    break;
                }
            }

            if (occupied) occupiedSlots++;
        }

        // The left edge needs a strict bar (nearly every row has text there, and a stray bright
        // pixel must not win); the right edge a lax one, since only the longest names reach it.
        int nameThreshold = Math.Max(2, occupiedSlots / 4);
        int nameRightThreshold = Math.Max(3, occupiedSlots / 8);
        int nameX = nameSearchStart;
        int nameRight = width;
        int firstTextColumn = -1;
        int lastTextColumn = -1;
        for (int x = nameSearchStart; x < width; x++)
        {
            if (lineColumn[x]) continue;
            if (firstTextColumn < 0 && slotHits[x] >= nameThreshold) firstTextColumn = x;
            if (slotHits[x] >= nameRightThreshold) lastTextColumn = x;
        }

        if (firstTextColumn >= 0)
        {
            nameX = Math.Max(nameSearchStart, firstTextColumn - 1);
            nameRight = Math.Min(width, lastTextColumn + 2);
        }
        else
        {
            nameX = Math.Min(width - 1, iconX + iconSize + 3);
            notes.Add("Not enough name text to measure the name column; used the standard offset.");
        }

        if (nameRight <= nameX)
        {
            nameRight = width;
        }

        // --- Top of the list viewport. This must be the real clipping edge: a partly visible
        // first row is the overflow signal (doc/PLAN.md §4.6) and has to stay inside ListRect.
        var content = new int[height];
        for (int y = 0; y < height; y++)
        {
            int count = 0;
            for (int x = iconX; x < nameRight; x++)
            {
                Bgr p = region.GetPixel(x, y);
                if (IsText(p) || IsIconLike(p)) count++;
            }

            content[y] = count;
        }

        int listTop = FindListTop(content, firstSlotTop, rowPitch, iconSize, height, opt.MaxUpwardWalk);

        PixelRect? headerCountRect = FindHeaderCountRect(region, content, listTop, width, IsText);
        if (headerCountRect is null)
        {
            notes.Add("No member-count digits found above the list; overflow will rely on the partial-row signal alone.");
        }

        var calibration = new Calibration(
            new PixelRect(0, listTop, width, height - listTop),
            iconX,
            nameX,
            nameRight,
            headerCountRect,
            rowPitch,
            iconSize);

        DetectionConfidence confidence = chosen.UsedColumnFallback || chosen.UsedPitchFallback || headerCountRect is null
            ? DetectionConfidence.Partial
            : DetectionConfidence.Good;

        notes.Insert(0, $"Icon column x={iconX} w={iconSize}, row pitch {rowPitch}px, " +
            $"name area x={nameX}..{nameRight}, list starts at y={listTop}, {chosen.ListIconRuns.Count} icons seen.");

        return new DetectionResult(calibration, confidence, notes);
    }

    private sealed record ColumnAnalysis(
        int IconX,
        int IconColumnWidth,
        int IconSize,
        int RowPitch,
        List<(int Start, int Length)> ListIconRuns,
        bool UsedPitchFallback,
        bool UsedColumnFallback,
        string? Rejection);

    private static ColumnAnalysis Reject(int x, int width, string reason) =>
        new(x, width, 0, 0, [], false, false, reason);

    /// <summary>Decides whether a candidate column really is a column of member-list icons.</summary>
    private static ColumnAnalysis AnalyseColumn(
        Frame region, int columnStart, int columnWidth, DetectorOptions opt, Func<Bgr, bool> isIconLike)
    {
        int height = region.Height;

        var iconRow = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = columnStart; x < columnStart + columnWidth; x++)
            {
                if (isIconLike(region.GetPixel(x, y)))
                {
                    iconRow[y] = true;
                    break;
                }
            }
        }

        List<(int Start, int Length)> candidateRuns = FindRuns(iconRow)
            .Where(r => r.Length >= opt.MinIconSize && r.Length <= opt.MaxIconSize)
            .ToList();

        if (candidateRuns.Count == 0)
        {
            return Reject(columnStart, columnWidth, "nothing icon-sized in it.");
        }

        int iconSize = Mode(candidateRuns.Select(r => r.Length)) ?? columnWidth;

        if (Math.Abs(columnWidth - iconSize) > opt.MaxIconAspectDrift)
        {
            return Reject(columnStart, columnWidth, $"blocks are {columnWidth}x{iconSize}, and icons are square.");
        }

        // A row clipped by the top of the viewport leaves a short icon run. Keeping it would put a
        // gap smaller than the real pitch into the list below, so only full-height icons count.
        List<(int Start, int Length)> iconRuns = candidateRuns
            .Where(r => Math.Abs(r.Length - iconSize) <= 1)
            .ToList();

        if (iconRuns.Count == 0)
        {
            iconRuns = candidateRuns;
        }

        double fill = MeanFill(region, columnStart, columnWidth, iconRuns, isIconLike);
        if (fill < opt.MinIconFillFraction)
        {
            return Reject(columnStart, columnWidth,
                $"only {fill:P0} of each block is filled, so it reads as text rather than icons.");
        }

        bool usedPitchFallback = false;
        int rowPitch;
        if (iconRuns.Count >= 2)
        {
            rowPitch = iconRuns.Zip(iconRuns.Skip(1), (a, b) => b.Start - a.Start).Min();
        }
        else
        {
            rowPitch = iconSize + 5;
            usedPitchFallback = true;
        }

        if (rowPitch < opt.MinRowPitch || rowPitch > opt.MaxRowPitch)
        {
            return Reject(columnStart, columnWidth, $"rows would be {rowPitch}px apart, outside the plausible range.");
        }

        if (rowPitch > iconSize + opt.MaxRowGap)
        {
            return Reject(columnStart, columnWidth,
                $"{rowPitch}px apart for {iconSize}px icons is too far apart for a member list.");
        }

        // The header's person glyph sits in the same columns as the icons, so drop any run that
        // doesn't share the list's row phase.
        int rowPhase = Mode(iconRuns.Select(r => r.Start % rowPitch)) ?? iconRuns[0].Start % rowPitch;
        List<(int Start, int Length)> listIconRuns = iconRuns.Where(r => r.Start % rowPitch == rowPhase).ToList();
        if (listIconRuns.Count == 0)
        {
            listIconRuns = iconRuns;
        }

        return new ColumnAnalysis(columnStart, columnWidth, iconSize, rowPitch, listIconRuns, usedPitchFallback, false, null);
    }

    private static double MeanFill(
        Frame region, int columnStart, int columnWidth, List<(int Start, int Length)> runs, Func<Bgr, bool> isIconLike)
    {
        double total = 0;
        foreach ((int start, int length) in runs)
        {
            int on = 0;
            for (int y = start; y < start + length; y++)
            {
                for (int x = columnStart; x < columnStart + columnWidth; x++)
                {
                    if (isIconLike(region.GetPixel(x, y))) on++;
                }
            }

            total += (double)on / (columnWidth * length);
        }

        return runs.Count == 0 ? 0 : total / runs.Count;
    }

    private static List<(int Start, int Length)> FindColumnRuns(int[] perColumn, DetectorOptions opt)
    {
        var flags = new bool[perColumn.Length];
        for (int x = 0; x < perColumn.Length; x++)
        {
            flags[x] = perColumn[x] > 0;
        }

        return FindRuns(flags)
            .Where(r => r.Length >= opt.MinIconSize && r.Length <= opt.MaxIconSize)
            .ToList();
    }

    private static List<int> BuildSlotTops(int referenceTop, int rowPitch, int iconSize, int height)
    {
        var tops = new List<int>();
        int first = referenceTop % rowPitch;
        for (int top = first; top + iconSize <= height; top += rowPitch)
        {
            tops.Add(top);
        }

        return tops;
    }

    private static int FindListTop(int[] content, int firstSlotTop, int rowPitch, int iconSize, int height, int maxWalk)
    {
        int top = firstSlotTop;
        for (int step = 0; step < maxWalk; step++)
        {
            int predicted = top - rowPitch;
            if (predicted < 0) break;

            int bandEnd = Math.Min(height, predicted + iconSize);
            int firstContent = -1;
            for (int y = Math.Max(0, predicted); y < bandEnd; y++)
            {
                if (content[y] > 0)
                {
                    firstContent = y;
                    break;
                }
            }

            // Nothing above: the current top is the first row.
            if (firstContent < 0) break;

            // Content that runs on above the slot isn't a row — it's the header block.
            if (predicted > 0 && content[predicted - 1] > 0) break;

            // An unclipped row starts its icon at +0 and its text at +2. Anything lower means the
            // row is cut off by the viewport edge, which is therefore exactly where content begins.
            if (firstContent - predicted > 2)
            {
                return firstContent;
            }

            top = predicted;
        }

        return top;
    }

    private static PixelRect? FindHeaderCountRect(Frame region, int[] content, int listTop, int width, Func<Bgr, bool> isText)
    {
        if (listTop <= 0) return null;

        var aboveList = new bool[listTop];
        for (int y = 0; y < listTop; y++)
        {
            aboveList[y] = content[y] > 0;
        }

        List<(int Start, int Length)> bands = FindRuns(aboveList);
        if (bands.Count == 0) return null;

        (int Start, int Length) header = bands[^1];
        int bandTop = header.Start;
        int bandBottom = header.Start + header.Length;

        var litColumn = new bool[width];
        for (int x = 0; x < width; x++)
        {
            for (int y = bandTop; y < bandBottom; y++)
            {
                if (isText(region.GetPixel(x, y)))
                {
                    litColumn[x] = true;
                    break;
                }
            }
        }

        List<(int Start, int Length)> runs = FindRuns(litColumn);
        if (runs.Count == 0) return null;

        // The person glyph is wider than a digit; drop it when it leads.
        if (runs[0].Length >= 9)
        {
            runs.RemoveAt(0);
        }

        if (runs.Count == 0) return null;

        int digitsStart = Math.Max(0, runs[0].Start - 2);
        int digitsEnd = Math.Min(width, runs[^1].Start + runs[^1].Length + 2);
        int rectTop = Math.Max(0, bandTop - 2);
        int rectBottom = Math.Min(listTop, bandBottom + 2);

        return new PixelRect(digitsStart, rectTop, digitsEnd - digitsStart, rectBottom - rectTop);
    }

    private static List<(int Start, int Length)> FindRuns(bool[] flags)
    {
        var runs = new List<(int Start, int Length)>();
        int i = 0;
        while (i < flags.Length)
        {
            if (!flags[i])
            {
                i++;
                continue;
            }

            int start = i;
            while (i < flags.Length && flags[i]) i++;
            runs.Add((start, i - start));
        }

        return runs;
    }

    private static int? Mode(IEnumerable<int> values)
    {
        var counts = new Dictionary<int, int>();
        foreach (int v in values)
        {
            counts[v] = counts.GetValueOrDefault(v) + 1;
        }

        if (counts.Count == 0) return null;

        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }
}
