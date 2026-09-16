namespace EveLocalMon.Core.Reading;

/// <summary>
/// Tunable detection thresholds. Defaults are set from pixel measurements of
/// doc/samples/*.png at 100% EVE UI scale / 100% Windows scaling — see doc/M1-SPEC.md §2.1.
/// </summary>
public sealed record ReaderOptions
{
    /// <summary>A pixel counts as "text" if its luma is at least this.</summary>
    public int TextLumaMin { get; init; } = 120;

    /// <summary>A row's name area needs at least this many text pixels to count as occupied.</summary>
    public int TextMinPixels { get; init; } = 15;

    /// <summary>A pixel counts as part of an icon if its luma is at least this...</summary>
    public int IconPixelLumaMin { get; init; } = 70;

    /// <summary>...or its chroma is at least this (catches saturated colours that aren't bright, e.g. dark red).</summary>
    public int IconPixelChromaMin { get; init; } = 40;

    /// <summary>Fraction of masked icon-slot pixels that must be icon-like for an icon to be considered present.</summary>
    public double IconPresentFraction { get; init; } = 0.5;

    /// <summary>Maximum mean per-channel colour difference (0-255) for a template match.
    /// Measured: the same icon differs by ≤2.2 across samples/backgrounds; the closest
    /// distinct icons differ by 17.3. 8.0 sits well inside that gap.</summary>
    public double MatchMaxMeanDiff { get; init; } = 8.0;

    /// <summary>Search window (in each of x and y) used to line the icon up with each template.</summary>
    public int MatchSearchRadius { get; init; } = 1;

    /// <summary>Minimum content pixels for a partially-visible row to count as "has content" (overflow signal).</summary>
    public int PartialRowMinPixels { get; init; } = 4;

    /// <summary>No-icon rows up to this count are assumed to be the active client's own pilot, not a threat.</summary>
    public int OwnPilotNoIconAllowance { get; init; } = 1;

    /// <summary>Luma threshold used to binarise header-count digits.</summary>
    public int DigitLumaMin { get; init; } = 120;

    /// <summary>Maximum fraction of mismatched pixels (against the padded bounding box) for a digit match.</summary>
    public double DigitMaxMismatchFraction { get; init; } = 0.15;
}
