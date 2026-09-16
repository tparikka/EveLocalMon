namespace EveLocalMon.Core.Imaging;

/// <summary>
/// A pixel's colour without alpha. Alpha is ignored everywhere in detection — captured
/// frames are always opaque and template PNGs use alpha only as an explicit compare mask.
/// </summary>
public readonly record struct Bgr(byte B, byte G, byte R)
{
    /// <summary>Perceptual brightness (ITU-R BT.601), 0-255. Used to tell text/icons from background.</summary>
    public int Luma => (R * 299 + G * 587 + B * 114) / 1000;

    /// <summary>Max-min channel spread, 0-255. High for saturated colours (icons), near 0 for gray/background.</summary>
    public int Chroma => Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B));
}
