using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Settings;

/// <summary>
/// Everything the app remembers between runs. <see cref="Calibration"/> is stored in region
/// coordinates (doc/M2-SPEC.md §2), so moving the EVE window only means re-picking the region.
/// Null means "use the built-in default".
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Screen-space region to capture, in physical pixels.</summary>
    public PixelRect? Region { get; init; }

    public Calibration? Calibration { get; init; }

    public int PollIntervalMs { get; init; } = 500;

    public string? TemplatesDirectory { get; init; }
}
