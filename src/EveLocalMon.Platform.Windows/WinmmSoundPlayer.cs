using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EveLocalMon.Core.Alerts;
using static EveLocalMon.Platform.Windows.NativeMethods;

namespace EveLocalMon.Platform.Windows;

/// <summary>
/// Plays a WAV held in memory through winmm's PlaySound — no NuGet package, and no temp file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinmmSoundPlayer : ISoundPlayer, IDisposable
{
    private GCHandle _pinned;

    public void Play(byte[] wav)
    {
        // SND_ASYNC returns immediately and Windows keeps reading this buffer while it plays, so
        // the bytes must stay pinned until playback ends. Releasing the previous handle here is
        // safe because a new PlaySound call stops whatever was playing.
        ReleasePinned();
        _pinned = GCHandle.Alloc(wav, GCHandleType.Pinned);

        if (!PlaySound(_pinned.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
        {
            ReleasePinned();
            throw new InvalidOperationException("PlaySound failed (is there an audio output device?).");
        }
    }

    public void Dispose()
    {
        PlaySound(IntPtr.Zero, IntPtr.Zero, SND_MEMORY | SND_ASYNC);
        ReleasePinned();
    }

    private void ReleasePinned()
    {
        if (_pinned.IsAllocated)
        {
            _pinned.Free();
        }
    }
}
