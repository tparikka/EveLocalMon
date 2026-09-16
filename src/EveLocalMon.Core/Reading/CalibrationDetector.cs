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
}

/// <summary>
/// Works out a <see cref="Calibration"/> from one captured region, so the user only has to drag a
/// rectangle around the Local pane. Everything it returns is in region coordinates and is meant to
/// be shown and overridable, never silently trusted — see doc/M2-SPEC.md §4.
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

        // --- 1. Icon column: colour icons are the only saturated thing in the pane; name text and
        // the gray neutral icon are unsaturated, so this isolates the icon column cleanly.
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

        bool usedIconColumnFallback = false;
        (int Start, int Length)? iconColumn = FindIconColumn(saturatedPerColumn, opt);
        if (iconColumn is null)
        {
            // A Local showing only gray/neutral icons has no saturated pixels at all.
            iconColumn = FindIconColumn(iconLikePerColumn, opt);
            usedIconColumnFallback = iconColumn is not null;
            if (usedIconColumnFallback)
            {
                notes.Add("No coloured icons found; icon column located from brightness instead — verify it.");
            }
        }

        if (iconColumn is null)
        {
            notes.Add("No icon column found. Is the region around the member list, and is Local visible?");
            return new DetectionResult(null, DetectionConfidence.Failed, notes);
        }

        int iconX = iconColumn.Value.Start;
        int iconColumnWidth = iconColumn.Value.Length;

        // --- 2. Icon rows in that column give exact slot tops, the icon size and the row pitch.
        var iconRow = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = iconX; x < iconX + iconColumnWidth; x++)
            {
                if (IsIconLike(region.GetPixel(x, y)))
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
            notes.Add("Found an icon column but no icons in it.");
            return new DetectionResult(null, DetectionConfidence.Failed, notes);
        }

        int iconSize = Mode(candidateRuns.Select(r => r.Length)) ?? iconColumnWidth;

        // A row clipped by the top of the viewport leaves a short icon run. Keeping it would put a
        // gap smaller than the real pitch into the list below, so only full-height icons count.
        List<(int Start, int Length)> iconRuns = candidateRuns
            .Where(r => Math.Abs(r.Length - iconSize) <= 1)
            .ToList();

        if (iconRuns.Count == 0)
        {
            iconRuns = candidateRuns;
        }

        bool usedPitchFallback = false;
        int rowPitch;
        if (iconRuns.Count >= 2)
        {
            // Gaps between icon starts are all multiples of the pitch (rows without icons just
            // skip one), so the smallest gap is the pitch.
            rowPitch = iconRuns.Zip(iconRuns.Skip(1), (a, b) => b.Start - a.Start).Min();
        }
        else
        {
            rowPitch = iconSize + 5;
            usedPitchFallback = true;
            notes.Add($"Only one icon visible; assumed a {rowPitch}px row pitch. Check it against a fuller Local.");
        }

        if (rowPitch < opt.MinRowPitch || rowPitch > opt.MaxRowPitch)
        {
            notes.Add($"Row pitch came out at {rowPitch}px, outside the plausible {opt.MinRowPitch}-{opt.MaxRowPitch}px range.");
            return new DetectionResult(null, DetectionConfidence.Failed, notes);
        }

        // The header's person glyph sits in the same columns as the icons, so drop any run that
        // doesn't share the list's row phase.
        int rowPhase = Mode(iconRuns.Select(r => r.Start % rowPitch)) ?? iconRuns[0].Start % rowPitch;
        List<(int Start, int Length)> listIconRuns = iconRuns.Where(r => r.Start % rowPitch == rowPhase).ToList();
        if (listIconRuns.Count == 0)
        {
            listIconRuns = iconRuns;
        }

        // --- 3. Name area. Slot-based counting keeps the header digits (above the list) and stray
        // bright pixels from being mistaken for name text.
        int firstSlotTop = listIconRuns[0].Start;
        List<int> slotTops = BuildSlotTops(firstSlotTop, rowPitch, iconSize, height);
        int nameSearchStart = Math.Min(width - 1, iconX + iconColumnWidth);

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

        // --- 4. Top of the list viewport. This must be the real clipping edge: a partly visible
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

        // --- 5. Header member count: inside the last content band above the list, the leftmost
        // wide run is the person glyph and the narrow runs after it are the digits.
        PixelRect? headerCountRect = FindHeaderCountRect(region, content, listTop, width, opt, IsText);
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

        DetectionConfidence confidence = usedIconColumnFallback || usedPitchFallback || headerCountRect is null
            ? DetectionConfidence.Partial
            : DetectionConfidence.Good;

        notes.Insert(0, $"Icon column x={iconX} w={iconSize}, row pitch {rowPitch}px, " +
            $"name area x={nameX}..{nameRight}, list starts at y={listTop}, {listIconRuns.Count} icons seen.");

        return new DetectionResult(calibration, confidence, notes);
    }

    private static (int Start, int Length)? FindIconColumn(int[] perColumn, DetectorOptions opt)
    {
        var flags = new bool[perColumn.Length];
        for (int x = 0; x < perColumn.Length; x++)
        {
            flags[x] = perColumn[x] > 0;
        }

        return FindRuns(flags)
            .Where(r => r.Length >= opt.MinIconSize && r.Length <= opt.MaxIconSize)
            .Select(r => ((int Start, int Length)?)r)
            .FirstOrDefault();
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

    private static PixelRect? FindHeaderCountRect(
        Frame region, int[] content, int listTop, int width, DetectorOptions opt, Func<Bgr, bool> isText)
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

        int best = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        return best;
    }
}
