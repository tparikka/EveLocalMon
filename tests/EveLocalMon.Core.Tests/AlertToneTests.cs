using System.Buffers.Binary;
using System.Text;
using EveLocalMon.Core.Audio;

namespace EveLocalMon.Core.Tests;

public class AlertToneTests
{
    [Fact]
    public void ProducesAValidRiffWaveHeader()
    {
        byte[] wav = AlertTone.BuildWav(100);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));  // PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));  // mono
        Assert.Equal(AlertTone.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34))); // bits per sample
    }

    [Fact]
    public void LengthMatchesTheRequestedDuration()
    {
        const int toneMs = 130;
        byte[] wav = AlertTone.BuildWav(100, toneMs: toneMs);

        int expectedSamples = 2 * (AlertTone.SampleRate * toneMs / 1000);
        Assert.Equal(44 + expectedSamples * 2, wav.Length);
        Assert.Equal(expectedSamples * 2, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(36 + expectedSamples * 2, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
    }

    [Fact]
    public void VolumeScalesAmplitude()
    {
        int loud = PeakAmplitude(AlertTone.BuildWav(100));
        int quiet = PeakAmplitude(AlertTone.BuildWav(50));

        Assert.InRange(quiet, loud / 2 - 400, loud / 2 + 400);
    }

    [Fact]
    public void ZeroVolumeIsSilenceNotAQuietChime()
    {
        Assert.Equal(0, PeakAmplitude(AlertTone.BuildWav(0)));
    }

    [Fact]
    public void VolumeOutsideZeroToOneHundredIsClamped()
    {
        Assert.Equal(PeakAmplitude(AlertTone.BuildWav(100)), PeakAmplitude(AlertTone.BuildWav(500)));
        Assert.Equal(0, PeakAmplitude(AlertTone.BuildWav(-20)));
    }

    private static int PeakAmplitude(byte[] wav)
    {
        int peak = 0;
        for (int offset = 44; offset + 1 < wav.Length; offset += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(offset))));
        }

        return peak;
    }
}
