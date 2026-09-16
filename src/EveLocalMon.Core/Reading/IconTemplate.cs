using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Reading;

public enum TemplateCategory
{
    Friendly,
    Enemy,
    Neutral,
}

/// <summary>
/// A cropped 12x12 icon used as a template. <see cref="Mask"/> says which of the
/// IconSize*IconSize pixels (row-major) participate in matching; corner pixels are excluded
/// by default because EVE's rounded-square icon corners anti-alias against varying backgrounds.
/// </summary>
public sealed record IconTemplate(string Name, TemplateCategory Category, Frame Image, bool[] Mask);
