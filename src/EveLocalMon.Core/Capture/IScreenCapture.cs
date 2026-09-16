using EveLocalMon.Core.Imaging;

namespace EveLocalMon.Core.Capture;

public interface IScreenCapture
{
    /// <summary>
    /// Captures the given region of the virtual desktop. Coordinates are physical pixels
    /// and may be negative (a monitor to the left of or above the primary monitor).
    /// </summary>
    Frame Capture(PixelRect region);
}
