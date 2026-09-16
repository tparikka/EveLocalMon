using System.Text.Json.Serialization;

namespace EveLocalMon.Core.Imaging;

/// <summary>
/// An axis-aligned rectangle in pixel coordinates. <see cref="Right"/> and <see cref="Bottom"/>
/// are exclusive, matching the half-open convention used throughout the reader.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore]
    public int Right => X + Width;

    [JsonIgnore]
    public int Bottom => Y + Height;
}
