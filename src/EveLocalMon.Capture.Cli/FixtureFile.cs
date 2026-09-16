using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Capture.Cli;

// Mirrors the fixture JSON schema in doc/M1-SPEC.md §4.3. Kept lightweight and separate
// from EveLocalMon.Core.Tests' copy — this one only needs to *load* a calibration for the
// debug `read` command, not assert every field.

internal sealed record RectDto(int X, int Y, int Width, int Height)
{
    public PixelRect ToPixelRect() => new(X, Y, Width, Height);
}

internal sealed record CalibrationDto(RectDto ListRect, int IconX, int NameX, int NameRight, RectDto? HeaderCountRect, int RowPitch = 17, int IconSize = 12)
{
    public Calibration ToCalibration() => new(ListRect.ToPixelRect(), IconX, NameX, NameRight, HeaderCountRect?.ToPixelRect(), RowPitch, IconSize);
}

internal sealed record CountsDto(int Friendly, int Enemy, int Neutral, int Unknown, int NoIcon);

internal sealed record ExpectedDto(
    int? PhaseFirstFullSlotTop,
    int OccupiedFullRows,
    bool HasPartialRowWithContent,
    int? HeaderMemberCount,
    string Readability,
    CountsDto? Counts,
    int ThreatTotal,
    string? RowCodes);

internal sealed record FixtureDto(string Image, CalibrationDto Calibration, ExpectedDto? Expected);
