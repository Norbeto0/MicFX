using MicFX.Audio;
using Xunit;

namespace MicFX.Tests;

public class VoiceGateTests
{
    private const int Frame = 480;

    private static float[] Ones(int channels) => Enumerable.Repeat(1f, Frame * channels).ToArray();

    [Fact]
    public void StartsClosed_OpensWithinAttack()
    {
        var gate = new VoiceGate(48000, 1, threshold: 0.5f, attackMs: 5f);
        gate.Reset();
        var f = Ones(1);
        gate.ProcessFrame(f, 0, Frame, 0.9f);
        Assert.True(f[0] < 0.01f, $"first sample {f[0]}"); // ramps up from closed (one step in)...
        Assert.Equal(1f, f[240], 3);                        // ...fully open after 5 ms (240 samples)
        Assert.Equal(1f, f[Frame - 1], 6);
    }

    [Fact]
    public void HoldsOpenThroughShortPauses_ThenReleases()
    {
        var gate = new VoiceGate(48000, 1, threshold: 0.5f, holdMs: 300, releaseMs: 80f);
        gate.Reset();
        for (int i = 0; i < 5; i++) gate.ProcessFrame(Ones(1), 0, Frame, 0.9f);

        for (int i = 0; i < 30; i++)                // 300 ms of non-speech: still held open
        {
            var f = Ones(1);
            gate.ProcessFrame(f, 0, Frame, 0.1f);
            Assert.Equal(1f, f[Frame - 1], 6);
        }
        for (int i = 0; i < 12; i++) gate.ProcessFrame(Ones(1), 0, Frame, 0.1f); // > 80 ms release
        var closed = Ones(1);
        gate.ProcessFrame(closed, 0, Frame, 0.1f);
        Assert.All(closed, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void GainNeverOvershoots_AndChannelsStayLinked()
    {
        var gate = new VoiceGate(48000, 2, threshold: 0.5f);
        gate.Reset();
        var rng = new Random(3);
        for (int i = 0; i < 400; i++)
        {
            var f = Ones(2);
            gate.ProcessFrame(f, 0, Frame, (float)rng.NextDouble());
            for (int s = 0; s < Frame; s++)
            {
                Assert.InRange(f[2 * s], 0f, 1f);
                Assert.Equal(f[2 * s], f[2 * s + 1]);
            }
        }
    }
}
