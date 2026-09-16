using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

/// <summary>
/// The one-time, per-user setup that tells the reader where the Local member list is and
/// how it's laid out. All coordinates are in the frame passed to <see cref="LocalListReader.Read"/>.
/// </summary>
/// <param name="ListRect">The member-list viewport, below the header row.</param>
/// <param name="IconX">Left x of the 12px icon column.</param>
/// <param name="NameX">Left x of the name text area (usually IconX + 15).</param>
/// <param name="NameRight">Exclusive right edge of the name area (stop before any scrollbar).</param>
/// <param name="HeaderCountRect">Digits-only rect for the member count, excluding the person icon. Null disables header-count reading.</param>
public sealed record Calibration(
    PixelRect ListRect,
    int IconX,
    int NameX,
    int NameRight,
    PixelRect? HeaderCountRect,
    int RowPitch = 17,
    int IconSize = 12);
