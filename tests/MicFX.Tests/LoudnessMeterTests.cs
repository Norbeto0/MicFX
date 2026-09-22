using MicFX.Audio;
using NAudio.Wave;
using Xunit;

namespace MicFX.Tests;

/// <summary>Reference cases from EBU Tech 3341 (loudness meter conformance).</summary>
public class LoudnessMeterTests
{
    private const int Rate = LoudnessMeter.SampleRate;

    private static float[] StereoSine(double seconds, double dbfs, double hz = 1000)
    {
        int n = (int)(seconds * Rate);
        float a = (float)Math.Pow(10, dbfs / 20);
        var x = new float[n * 2];
        for (int i = 0; i < n; i++)
            x[2 * i] = x[2 * i + 1] = a * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        return x;
    }

    private static double Measure(params float[][] segments)
    {
        var m = new LoudnessMeter();
        foreach (var s in segments) m.Add(s, s.Length);
        return m.IntegratedLufs() ?? double.NaN;
    }

    [Theory]
    [InlineData(-23.0)] // EBU 3341 case 1
    [InlineData(-33.0)] // EBU 3341 case 2
    public void StereoSine_ReadsItsLevel(double dbfs) =>
        Assert.InRange(Measure(StereoSine(20, dbfs)), dbfs - 0.1, dbfs + 0.1);

    [Fact]
    public void RelativeGate_IgnoresQuietPassages() =>
        // EBU 3341 case 3: -36 / -23 / -36 dBFS for 10 / 60 / 10 s reads -23.0
        Assert.InRange(Measure(StereoSine(10, -36), StereoSine(60, -23), StereoSine(10, -36)), -23.1, -22.9);

    [Fact]
    public void AbsoluteGate_IgnoresSilence() =>
        Assert.InRange(Measure(StereoSine(20, -23), new float[Rate * 2 * 20]), -23.1, -22.9);

    [Fact]
    public void ClipShorterThanOneBlock_StillMeasured() =>
        Assert.InRange(Measure(StereoSine(0.2, -23)), -23.5, -22.5);

    [Fact]
    public void Silence_HasNoLoudness() =>
        Assert.Null(new LoudnessMeter().IntegratedLufs());

    [Fact]
    public void MonoFile_IsMeasuredAsPlayed_ResampledAndUpmixed()
    {
        // 44.1 kHz mono file; played duplicated to both channels, so it should
        // read like a stereo sine of the same level.
        string path = Path.Combine(Path.GetTempPath(), $"micfx-mono-{Guid.NewGuid():N}.wav");
        try
        {
            using (var w = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
            {
                float a = (float)Math.Pow(10, -23 / 20.0);
                var buf = new float[44100 * 10];
                for (int i = 0; i < buf.Length; i++) buf[i] = a * (float)Math.Sin(2 * Math.PI * 1000 * i / 44100.0);
                w.WriteSamples(buf, 0, buf.Length);
            }
            Assert.InRange(LoudnessMeter.MeasureFile(path) ?? double.NaN, -23.2, -22.8);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-20.0, 1.0)]     // already at target
    [InlineData(-10.0, 0.3162)]  // loud clip turned down 10 dB
    [InlineData(-26.0, 1.9953)]  // quiet clip raised 6 dB
    [InlineData(-45.0, 3.9811)]  // boost capped at +12 dB
    public void NormalizationGain_TargetsMinus20_WithBoostCap(double lufs, double expected) =>
        Assert.Equal(expected, LoudnessMeter.NormalizationGain(lufs), 3);
}
