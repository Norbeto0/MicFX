using MicFX.Audio;
using Xunit;

namespace MicFX.Tests;

public class ClassicDspTests
{
    private const int Rate = TestSignals.Rate;

    // "Speech": modulated 220 Hz harmonic bursts, 0.5 s on / 0.5 s off.
    private static float Speech(long n)
    {
        double t = n / (double)Rate;
        if ((long)(t * 2) % 2 != 0) return 0f;
        double env = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 3.1 * t);
        return (float)(env * (0.25 * Math.Sin(2 * Math.PI * 220 * t)
                            + 0.12 * Math.Sin(2 * Math.PI * 440 * t)
                            + 0.06 * Math.Sin(2 * Math.PI * 880 * t)));
    }

    [Fact]
    public void SpectralSuppressor_KeepsSpeech_CutsNoiseFloor()
    {
        var rng = new Random(42);
        var mirror = new Random(42);
        var dn = new NoiseSuppressionSampleProvider(
            TestSignals.Mono(n => Speech(n) + 0.03f * (float)(rng.NextDouble() * 2 - 1)))
        { Enabled = true, ReductionDb = 18f };

        var buf = new float[480];
        double sIn = 0, sOut = 0, qIn = 0, qOut = 0;
        long nS = 0, nQ = 0, sample = 0;
        for (int b = 0; b < Rate * 10 / 480; b++)
        {
            int read = dn.Read(buf, 0, buf.Length);
            for (int i = 0; i < read; i++, sample++)
            {
                float input = Speech(sample) + 0.03f * (float)(mirror.NextDouble() * 2 - 1);
                bool burst = (long)(Math.Max(0, sample - 256) / (double)Rate * 2) % 2 == 0; // 256 = hop delay
                if (sample <= Rate * 2) continue;
                if (burst) { sOut += buf[i] * buf[i]; sIn += input * input; nS++; }
                else { qOut += buf[i] * buf[i]; qIn += input * input; nQ++; }
            }
        }
        double speechDelta = 10 * Math.Log10(sOut / sIn);
        double noiseDelta = 10 * Math.Log10(qOut / qIn);
        Assert.True(speechDelta > -3, $"speech attenuated {speechDelta:F1} dB");
        Assert.True(noiseDelta < -10, $"noise only cut {noiseDelta:F1} dB");
    }

    [Fact]
    public void SpectralSuppressor_Bypass_IsBitExact()
    {
        var dn = new NoiseSuppressionSampleProvider(TestSignals.Mono(n => (float)Math.Sin(2 * Math.PI * 440 * n / Rate) * 0.5f));
        var b = new float[4800];
        dn.Read(b, 0, b.Length);
        for (int i = 0; i < b.Length; i++)
            Assert.Equal((float)Math.Sin(2 * Math.PI * 440 * i / Rate) * 0.5f, b[i], 6);
    }

    [Fact]
    public void Compressor_BringsQuietAndLoudCloser()
    {
        static float Peak(float amp)
        {
            var c = new CompressorSampleProvider(TestSignals.Mono(n => (float)Math.Sin(2 * Math.PI * 200 * n / Rate) * amp)) { Enabled = true };
            c.SetAmount(50);
            var b = new float[Rate];
            c.Read(b, 0, b.Length);
            c.Read(b, 0, b.Length);
            return b.Max(Math.Abs);
        }
        float quiet = Peak(0.05f), loud = Peak(0.8f);
        Assert.True(quiet > 0.05f);
        Assert.True(loud <= 1.0f);
        Assert.True(loud / quiet < (0.8 / 0.05) / 2);
    }
}
