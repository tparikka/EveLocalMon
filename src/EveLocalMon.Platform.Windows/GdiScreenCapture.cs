using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EveLocalMon.Core.Capture;
using EveLocalMon.Core.Imaging;
using static EveLocalMon.Platform.Windows.NativeMethods;

namespace EveLocalMon.Platform.Windows;

/// <summary>
/// Captures a region of the virtual desktop with GDI BitBlt. Simple and fast enough for
/// polling at a few Hz; does not see through windows that cover the target (that would
/// need Windows.Graphics.Capture, a later upgrade if EVE is ever obscured).
///
/// The calling process must be per-monitor DPI aware (V2) — see App.manifest in
/// EveLocalMon.Capture.Cli — or the coordinates BitBlt uses will not be physical pixels.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiScreenCapture : IScreenCapture, IDisposable
{
    private IntPtr _memDc;
    private IntPtr _hBitmap;
    private IntPtr _oldBitmap;
    private IntPtr _dibBits;
    private int _dibWidth;
    private int _dibHeight;

    public Frame Capture(PixelRect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Region must have positive width and height.");
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC(NULL) failed.");
        }

        try
        {
            EnsureDib(screenDc, region.Width, region.Height);

            bool ok = BitBlt(_memDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, SRCCOPY | CAPTUREBLT);
            if (!ok)
            {
                throw new InvalidOperationException($"BitBlt failed (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            // 32-bpp DIB rows are never padded (4 bytes/pixel is already a multiple of 4).
            int stride = region.Width * 4;
            byte[] pixels = new byte[stride * region.Height];
            Marshal.Copy(_dibBits, pixels, 0, pixels.Length);
            return new Frame(region.Width, region.Height, stride, pixels);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void EnsureDib(IntPtr screenDc, int width, int height)
    {
        if (_hBitmap != IntPtr.Zero && _dibWidth == width && _dibHeight == height)
        {
            return;
        }

        FreeDib();

        _memDc = CreateCompatibleDC(screenDc);
        if (_memDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateCompatibleDC failed.");
        }

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative => top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            },
        };

        _hBitmap = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
        if (_hBitmap == IntPtr.Zero || _dibBits == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        _oldBitmap = SelectObject(_memDc, _hBitmap);
        _dibWidth = width;
        _dibHeight = height;
    }

    private void FreeDib()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }

        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        _dibBits = IntPtr.Zero;
        _dibWidth = 0;
        _dibHeight = 0;
    }

    public void Dispose() => FreeDib();
}
