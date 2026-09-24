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

    [Theory]
    [InlineData(18f)]
    [InlineData(30f)]
    public void SpectralSuppressor_CutsSteadyNoiseByStrength(float reductionDb)
    {
        var rng = new Random(5);
        var dn = new NoiseSuppressionSampleProvider(TestSignals.Mono(_ => 0.02f * (float)(rng.NextDouble() * 2 - 1)))
        { Enabled = true, ReductionDb = reductionDb };
        var y = TestSignals.ReadAll(dn, Rate * 6);
        double inputDb = 20 * Math.Log10(0.02 / Math.Sqrt(3)); // uniform noise RMS
        double delta = TestSignals.RmsDb(y.AsSpan(Rate * 2)) - inputDb;
        Assert.InRange(delta, -reductionDb - 1.5, -reductionDb + 1.5);
    }

    /// <summary>
    /// Musical noise is residual noise whose spectrum is spikier than the noise
    /// itself: isolated bins flickering on. Compare the spectral kurtosis of the
    /// output in the pauses with that of the input noise there (log ratio 0 =
    /// same character, only quieter).
    /// </summary>
    [Fact]
    public void SpectralSuppressor_PausesHaveNoMusicalNoise()
    {
        var rng = new Random(9);
        var noise = new float[Rate * 10];
        for (int i = 0; i < noise.Length; i++) noise[i] = 0.01f * (float)(rng.NextDouble() * 2 - 1);
        var input = new float[noise.Length];
        for (int i = 0; i < input.Length; i++) input[i] = Speech(i) + noise[i];

        var dn = new NoiseSuppressionSampleProvider(TestSignals.FromArray(input)) { Enabled = true, ReductionDb = 18f };
        var y = TestSignals.ReadAll(dn, input.Length);

        // Speech() is silent in the second half of each second. Skip the first
        // 60 ms of each pause (the tail of the burst and the detector's
        // hangover, masked by the preceding sound anyway) and the last 10 ms,
        // as the benchmark does.
        const int delay = 256, half = Rate / 2, head = Rate * 60 / 1000, tail = Rate / 100;
        var outPauses = new List<float[]>();
        var noisePauses = new List<float[]>();
        for (int sec = 2; sec < 9; sec++)
        {
            int start = sec * Rate + half + head, length = half - head - tail;
            outPauses.Add(y.AsSpan(start + delay, length).ToArray());
            noisePauses.Add(noise.AsSpan(start, length).ToArray());
        }
        double lkr = Math.Log(SpectralKurtosis(outPauses) / SpectralKurtosis(noisePauses));
        Assert.True(lkr < 0.25, $"log kurtosis ratio {lkr:F2}");
    }

    private static double SpectralKurtosis(IEnumerable<float[]> segments)
    {
        const int n = 512;
        var buf = new NAudio.Dsp.Complex[n];
        var powers = new List<double>();
        foreach (var seg in segments)
        {
            for (int start = 0; start + n <= seg.Length; start += n / 2)
            {
                for (int i = 0; i < n; i++)
                {
                    buf[i].X = seg[start + i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
                    buf[i].Y = 0f;
                }
                NAudio.Dsp.FastFourierTransform.FFT(true, 9, buf);
                for (int b = 5; b < 200; b++) powers.Add(buf[b].X * (double)buf[b].X + buf[b].Y * (double)buf[b].Y);
            }
        }
        double mean = powers.Average();
        double m2 = powers.Average(p => (p - mean) * (p - mean));
        double m4 = powers.Average(p => Math.Pow(p - mean, 4));
        return m4 / (m2 * m2);
    }

    /// <summary>
    /// Same algorithm as the benchmark that chose it: output of the Python
    /// reference (scratch benchmark gen_spectral_parity.py) on real speech in
    /// fan noise. Runs only when MICFX_PARITY_DIR points at those files.
    /// </summary>
    [Theory]
    [InlineData(18f)]
    [InlineData(30f)]
    public void SpectralSuppressor_MatchesBenchmarkedReference(float reductionDb)
    {
        var dir = Environment.GetEnvironmentVariable("MICFX_PARITY_DIR");
        if (dir == null) return;
        var input = TestSignals.ReadF32(Path.Combine(dir, "spectral_in.f32"));
        var reference = TestSignals.ReadF32(Path.Combine(dir, $"spectral_ref_{reductionDb:0}.f32"));
        var dn = new NoiseSuppressionSampleProvider(TestSignals.FromArray(input)) { Enabled = true, ReductionDb = reductionDb };
        var y = TestSignals.ReadAll(dn, reference.Length);
        Assert.Equal(reference.Length, y.Length);
        double maxErr = 0;
        for (int i = 0; i < reference.Length; i++) maxErr = Math.Max(maxErr, Math.Abs(y[i] - reference[i]));
        Assert.True(maxErr < 1e-4, $"{reductionDb} dB: max deviation {maxErr:E2}");
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

    /// <summary>The leveler's make-up gain is for the voice: near-silence (room noise between words) is not lifted.</summary>
    [Theory]
    [InlineData(0.0005f, 0.0, 1.0)]    // -66 dBFS: left alone
    [InlineData(0.05f, 8.0, 14.0)]     // -26 dBFS: full make-up (13.75 dB at 100, minus a little compression)
    public void Compressor_LiftsVoiceNotSilence(float amplitude, double minGainDb, double maxGainDb)
    {
        var c = new CompressorSampleProvider(TestSignals.Mono(n => (float)Math.Sin(2 * Math.PI * 200 * n / Rate) * amplitude)) { Enabled = true };
        c.SetAmount(100);
        var b = new float[Rate];
        c.Read(b, 0, b.Length);
        c.Read(b, 0, b.Length);
        double gainDb = 20 * Math.Log10(b.Max(Math.Abs) / amplitude);
        Assert.InRange(gainDb, minGainDb, maxGainDb);
    }
}
