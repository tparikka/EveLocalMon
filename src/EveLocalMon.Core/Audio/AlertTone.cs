using System.Buffers.Binary;
using System.Text;

namespace EveLocalMon.Core.Audio;

/// <summary>
/// Synthesises the built-in alert chime as a 16-bit mono PCM WAV, in memory.
///
/// Generating it rather than shipping a file keeps a binary blob (and its licensing question) out
/// of the repo, and makes the volume control possible at all: there is no volume knob on the
/// Win32 sound API used to play it, so volume has to be baked into the samples.
/// </summary>
public static class AlertTone
{
    public const int SampleRate = 44100;
    private const int HeaderBytes = 44;

    public static byte[] BuildWav(int volumePercent, double firstHz = 880, double secondHz = 1175, int toneMs = 130)
    {
        int volume = Math.Clamp(volumePercent, 0, 100);
        int samplesPerTone = SampleRate * toneMs / 1000;
        int totalSamples = samplesPerTone * 2;
        int dataBytes = totalSamples * 2;

        var buffer = new byte[HeaderBytes + dataBytes];
        WriteHeader(buffer, dataBytes);

        double amplitude = short.MaxValue * 0.6 * (volume / 100.0);
        int fadeSamples = SampleRate / 200; // ~5 ms, enough to stop the click at each edge

        for (int i = 0; i < totalSamples; i++)
        {
            bool second = i >= samplesPerTone;
            int withinTone = second ? i - samplesPerTone : i;
            double frequency = second ? secondHz : firstHz;

            double envelope = 1.0;
            if (withinTone < fadeSamples) envelope = withinTone / (double)fadeSamples;
            else if (withinTone > samplesPerTone - fadeSamples) envelope = (samplesPerTone - withinTone) / (double)fadeSamples;

            double sample = Math.Sin(2 * Math.PI * frequency * withinTone / SampleRate) * amplitude * Math.Max(0, envelope);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(HeaderBytes + i * 2), (short)sample);
        }

        return buffer;
    }

    private static void WriteHeader(byte[] buffer, int dataBytes)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = SampleRate * channels * bitsPerSample / 8;

        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, 8);

        Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, 12);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 16);           // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(20), 1);            // PCM
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(32), channels * bitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(34), bitsPerSample);

        Encoding.ASCII.GetBytes("data").CopyTo(buffer, 36);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(40), dataBytes);
    }
}
