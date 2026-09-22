using MicFX.Audio;
using Xunit;

namespace MicFX.Tests;

/// <summary>
/// Needs the native library, embedded from src/MicFX/native (built by CI) or a
/// path passed as -p:RnNoiseLib=... . Tests pass trivially when it is absent,
/// unless MICFX_REQUIRE_RNNOISE=1 — CI sets that, so a missing or broken
/// library fails the release instead of being skipped.
/// </summary>
public class RnNoiseTests
{
    private const int Rate = TestSignals.Rate;

    private static bool Ready()
    {
        bool required = Environment.GetEnvironmentVariable("MICFX_REQUIRE_RNNOISE") == "1";
        if (required)
            Assert.True(RnNoiseSampleProvider.IsAvailable, $"RNNoise unavailable: {RnNoiseSampleProvider.UnavailableReason}");
        return RnNoiseSampleProvider.IsAvailable;
    }

    /// <summary>Quiet low-passed noise, shaped like fan / AC rumble.</summary>
    private static float[] FanNoise(int samples, int seed = 7)
    {
        var rng = new Random(seed);
        float lp = 0f;
        var x = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            lp = 0.92f * lp + 0.08f * (float)(rng.NextDouble() * 2 - 1);
            x[i] = lp * 0.1f;
        }
        return x;
    }

    [Fact]
    public void Bypass_IsBitExact()
    {
        if (!Ready()) return;
        using var dn = new RnNoiseSampleProvider(TestSignals.Mono(n => (float)Math.Sin(2 * Math.PI * 440 * n / Rate) * 0.5f)) { Enabled = false };
        var b = new float[4800];
        dn.Read(b, 0, b.Length);
        for (int i = 0; i < b.Length; i++)
            Assert.Equal((float)Math.Sin(2 * Math.PI * 440 * i / Rate) * 0.5f, b[i], 6);
    }

    [Fact]
    public void RemovesFanNoise()
    {
        if (!Ready()) return;
        var noise = FanNoise(Rate * 12);
        using var dn = new RnNoiseSampleProvider(TestSignals.FromArray(noise)) { Enabled = true };
        var y = TestSignals.ReadAll(dn, noise.Length);
        int skip = Rate * 4;
        double delta = TestSignals.RmsDb(y.AsSpan(skip)) - TestSignals.RmsDb(noise.AsSpan(skip));
        Assert.True(y.All(v => !float.IsNaN(v) && Math.Abs(v) < 1.5f));
        Assert.True(delta < -20, $"fan noise only reduced {delta:F1} dB");
    }

    [Fact]
    public void StereoChannelsStaySeparate()
    {
        if (!Ready()) return;
        using var dn = new RnNoiseSampleProvider(TestSignals.Stereo(n => ((float)Math.Sin(2 * Math.PI * 300 * n / Rate) * 0.4f, 0f))) { Enabled = true };
        var y = TestSignals.ReadAll(dn, 960 * 100);
        double left = 0, right = 0;
        for (int i = 0; i + 1 < y.Length; i += 2) { left += y[i] * y[i]; right += y[i + 1] * y[i + 1]; }
        Assert.True(right < left / 100 + 1e-9, $"left {left}, right {right}");
    }

    /// <summary>
    /// The strength blend must be exactly (1-d)·wet + d·input delayed by the
    /// model's latency. An undelayed dry path would comb-filter the voice.
    /// </summary>
    [Theory]
    [InlineData(24f)]
    [InlineData(12f)]
    [InlineData(6f)]
    public void StrengthBlend_IsDelayMatched(float maxReductionDb)
    {
        if (!Ready()) return;
        var rng = new Random(11);
        var input = new float[Rate * 3];
        for (int i = 0; i < input.Length; i++)
            input[i] = 0.2f * (float)Math.Sin(2 * Math.PI * 180 * i / Rate) * (float)(0.5 + 0.5 * Math.Sin(2 * Math.PI * 2 * i / Rate))
                     + 0.02f * (float)(rng.NextDouble() * 2 - 1);

        using var wetOnly = new RnNoiseSampleProvider(TestSignals.FromArray(input)) { Enabled = true };
        using var blended = new RnNoiseSampleProvider(TestSignals.FromArray(input)) { Enabled = true, MaxReductionDb = maxReductionDb };
        var wet = TestSignals.ReadAll(wetOnly, input.Length);
        var mix = TestSignals.ReadAll(blended, input.Length);

        float d = MathF.Pow(10f, -maxReductionDb / 20f);
        int delay = RnNoiseSampleProvider.AlgorithmicDelay;
        double maxErr = 0;
        for (int i = 0; i < mix.Length; i++)
        {
            float dry = i >= delay ? input[i - delay] : 0f;
            maxErr = Math.Max(maxErr, Math.Abs(mix[i] - ((1 - d) * wet[i] + d * dry)));
        }
        Assert.True(maxErr < 1e-5, $"blend deviates by {maxErr:E2}");
    }

    [Fact]
    public void VoiceGate_SilencesNoiseOnlyInput()
    {
        if (!Ready()) return;
        var noise = FanNoise(Rate * 6, seed: 5);
        using var dn = new RnNoiseSampleProvider(TestSignals.FromArray(noise)) { Enabled = true, VoiceGateEnabled = true, MaxReductionDb = 12f };
        var y = TestSignals.ReadAll(dn, noise.Length);
        // Even with the dry path letting noise through at -12 dB, the gate
        // should hold the output at silence since nothing is speech.
        double level = TestSignals.RmsDb(y.AsSpan(Rate));
        Assert.True(level < TestSignals.RmsDb(noise.AsSpan(Rate)) - 40, $"gated output at {level:F1} dB");
    }

    /// <summary>
    /// Optional: compares against the Python reference used for the benchmark
    /// (real speech + keyboard noise). Set MICFX_PARITY_DIR to a folder holding
    /// parity_in.f32 and parity_ref_{max,24}.f32.
    /// </summary>
    [Theory]
    [InlineData("max", 60f)]
    [InlineData("24", 24f)]
    public void MatchesBenchmarkedReference(string label, float maxReductionDb)
    {
        var dir = Environment.GetEnvironmentVariable("MICFX_PARITY_DIR");
        if (dir == null || !Ready()) return;
        var input = ReadF32(Path.Combine(dir, "parity_in.f32"));
        var reference = ReadF32(Path.Combine(dir, $"parity_ref_{label}.f32"));
        using var dn = new RnNoiseSampleProvider(TestSignals.FromArray(input)) { Enabled = true, MaxReductionDb = maxReductionDb };
        var y = TestSignals.ReadAll(dn, reference.Length);
        double maxErr = 0;
        for (int i = 0; i < reference.Length; i++) maxErr = Math.Max(maxErr, Math.Abs(y[i] - reference[i]));
        Assert.True(maxErr < 1e-4, $"{label}: max deviation {maxErr:E2}");
    }

    private static float[] ReadF32(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }
}
