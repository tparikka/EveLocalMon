namespace EveLocalMon.Core.Alerts;

/// <summary>Plays a WAV. The dispatcher decides which bytes (built-in tone or the user's file),
/// so the platform layer stays a thin call into the OS.</summary>
public interface ISoundPlayer
{
    void Play(byte[] wav);
}

/// <summary>Shows the alert on screen. Implemented by the app's popup window.</summary>
public interface IAlertPresenter
{
    void Present(AlertMessage message);
}

public sealed record NtfyOptions(string Server, string Topic);

public interface INtfyClient
{
    /// <summary>Returns false rather than throwing: a push failing must never stop the sound and
    /// popup, which are the channels the user is actually looking at.</summary>
    Task<bool> SendAsync(AlertMessage message, NtfyOptions options, CancellationToken cancellationToken = default);
}
