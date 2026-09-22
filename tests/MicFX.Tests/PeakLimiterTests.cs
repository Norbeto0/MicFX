using MicFX.Audio;
using Xunit;

namespace MicFX.Tests;

public class PeakLimiterTests
{
    private const int Rate = TestSignals.Rate;
    private static readonly float Ceiling = MathF.Pow(10f, PeakLimiterSampleProvider.CeilingDb / 20f);

    [Fact]
    public void LoudSine_NeverExceedsCeiling_AndIsNotOverLimited()
    {
        var lim = new PeakLimiterSampleProvider(TestSignals.Mono(n => 2f * (float)Math.Sin(2 * Math.PI * 440 * n / Rate))); // +6 dBFS
        var y = TestSignals.ReadAll(lim, Rate * 2);
        float peak = y.Skip(Rate / 2).Max(Math.Abs);
        Assert.True(y.All(v => Math.Abs(v) <= Ceiling + 1e-6f), $"peak {y.Max(Math.Abs)}");
        Assert.True(peak > Ceiling * 0.95f, $"over-limited to {peak}");
        Assert.True(lim.ConsumeMaxGainReductionDb() > 5f);
    }

    [Fact]
    public void Transients_NeverExceedCeiling()
    {
        var rng = new Random(9);
        var x = new float[Rate * 3];
        for (int i = 0; i < x.Length; i++) x[i] = 0.1f * (float)(rng.NextDouble() * 2 - 1);
        for (int k = 0; k < 200; k++) x[rng.Next(x.Length)] = (float)(rng.NextDouble() * 8 - 4); // spikes to ±4
        var lim = new PeakLimiterSampleProvider(TestSignals.FromArray(x));
        var y = TestSignals.ReadAll(lim, x.Length);
        Assert.True(y.All(v => Math.Abs(v) <= Ceiling + 1e-6f));
    }

    [Fact]
    public void QuietSignal_PassesBitExact_AfterLookaheadDelay()
    {
        var x = new float[Rate];
        for (int i = 0; i < x.Length; i++) x[i] = 0.1f * (float)Math.Sin(2 * Math.PI * 300 * i / Rate); // -20 dBFS
        var lim = new PeakLimiterSampleProvider(TestSignals.FromArray(x));
        var y = TestSignals.ReadAll(lim, x.Length);
        int d = lim.LatencySamples;
        Assert.Equal(48, d);
        for (int i = 0; i < d; i++) Assert.Equal(0f, y[i]);
        for (int i = d; i < y.Length; i++) Assert.Equal(x[i - d], y[i]);
        Assert.Equal(0f, lim.ConsumeMaxGainReductionDb());
    }

    [Fact]
    public void StereoGainIsLinked()
    {
        // Loud left, quiet right: both reduced by the same gain, so the ratio holds.
        var lim = new PeakLimiterSampleProvider(TestSignals.Stereo(n =>
        {
            float s = (float)Math.Sin(2 * Math.PI * 250 * n / Rate);
            return (2f * s, 0.2f * s);
        }));
        var y = TestSignals.ReadAll(lim, Rate * 2 * 2);
        for (int i = Rate * 2; i + 1 < y.Length; i += 2)
            if (Math.Abs(y[i]) > 0.05f && Math.Abs(y[i]) < Ceiling * 0.999f)
                Assert.Equal(10f, y[i] / y[i + 1], 2);
    }
}
