using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests.Support;

// Mirrors the fixture JSON schema in doc/M1-SPEC.md §4.3.

public sealed record RectDto(int X, int Y, int Width, int Height)
{
    public PixelRect ToPixelRect() => new(X, Y, Width, Height);
}

public sealed record CalibrationDto(RectDto ListRect, int IconX, int NameX, int NameRight, RectDto? HeaderCountRect, int RowPitch = 17, int IconSize = 12)
{
    public Calibration ToCalibration() => new(ListRect.ToPixelRect(), IconX, NameX, NameRight, HeaderCountRect?.ToPixelRect(), RowPitch, IconSize);
}

public sealed record CountsDto(int Friendly, int Enemy, int Neutral, int Unknown, int NoIcon);

public sealed record ExpectedDto(
    int? PhaseFirstFullSlotTop,
    int OccupiedFullRows,
    bool HasPartialRowWithContent,
    int? HeaderMemberCount,
    string Readability,
    CountsDto? Counts,
    int ThreatTotal,
    string? RowCodes);

public sealed record FixtureDto(string Image, CalibrationDto Calibration, ExpectedDto? Expected);
