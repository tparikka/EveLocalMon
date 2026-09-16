namespace EveLocalMon.Core.Reading;

internal static class IconMasking
{
    /// <summary>
    /// All pixels of a size*size icon except the 4 corners, which anti-alias against
    /// whatever background shows through EVE's translucent Local pane.
    /// </summary>
    public static bool[] Default(int size)
    {
        var mask = new bool[size * size];
        Array.Fill(mask, true);
        mask[0] = false;
        mask[size - 1] = false;
        mask[(size - 1) * size] = false;
        mask[(size - 1) * size + size - 1] = false;
        return mask;
    }
}
