namespace EveLocalMon.Core.Reading;

/// <summary>
/// All icon and digit templates used by the reader. Icons are matched by masked colour
/// difference (see <see cref="LocalListReader"/>); digits are matched by a binary
/// (lit/unlit) pixel grid, keyed by the character they represent.
/// </summary>
public sealed record TemplateSet(IReadOnlyList<IconTemplate> Icons, IReadOnlyDictionary<char, bool[,]> Digits);
