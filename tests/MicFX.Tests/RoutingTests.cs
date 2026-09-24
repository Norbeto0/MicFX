using MicFX.Audio;
using NAudio.Wave;
using Xunit;

namespace MicFX.Tests;

/// <summary>Drift compensation, input selection, loop guard, silence detection.</summary>
public class RoutingTests
{
    [Fact]
    public void Drift_KeepsBacklogBounded_WhenMicRunsFast()
    {
        var fmt = new WaveFormat(48000, 16, 2);
        var buffer = new BufferedWaveProvider(fmt) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true };
        var drift = new DriftCompensatingSampleProvider(buffer, targetMs: 40, ceilingMs: 140);
        var rng = new Random(1);
        var chunk = new byte[512 * 4];
        var outBuf = new float[960];
        double pending = 0, maxMs = 0;
        for (int i = 0; i < 60000; i++) // 600 s at 10 ms steps, producer 0.3% fast
        {
            pending += 480 * 1.003;
            int push = (int)pending; pending -= push;
            rng.NextBytes(chunk.AsSpan(0, push * 4));
            buffer.AddSamples(chunk, 0, push * 4);
            drift.Read(outBuf, 0, outBuf.Length);
            if (i > 100) maxMs = Math.Max(maxMs, buffer.BufferedBytes / (double)fmt.AverageBytesPerSecond * 1000);
        }
        Assert.True(drift.Corrections > 0);
        Assert.True(maxMs <= 145, $"backlog reached {maxMs:F0} ms");
    }

    [Fact]
    public void Drift_CorrectionNeverInsertsSilence()
    {
        var fmt = new WaveFormat(48000, 16, 2);
        var buffer = new BufferedWaveProvider(fmt) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true };
        var drift = new DriftCompensatingSampleProvider(buffer, targetMs: 40, ceilingMs: 90); // "Lowest" bounds
        var chunk = new byte[512 * 4];
        for (int i = 0; i < chunk.Length; i += 2) { chunk[i] = 0xE8; chunk[i + 1] = 0x03; } // constant non-zero
        var outBuf = new float[960];
        double pending = 0;
        int zeros = 0;
        for (int i = 0; i < 20000; i++)
        {
            pending += 480 * 1.003;
            int push = (int)pending; pending -= push;
            buffer.AddSamples(chunk, 0, push * 4);
            int read = drift.Read(outBuf, 0, outBuf.Length);
            if (i > 50) zeros += outBuf.Take(read).Count(v => v == 0f);
        }
        Assert.True(drift.Corrections > 0);
        Assert.Equal(0, zeros);
    }

    [Theory]
    [InlineData(false, new[] { "desk", "quest" }, "desk")]   // VD mic present but idle
    [InlineData(true, new[] { "desk", "quest" }, "quest")]   // carrying audio -> switch
    [InlineData(true, new[] { "desk" }, "desk")]             // unplugged
    [InlineData(false, new[] { "quest" }, "quest")]          // only idle preferred left
    [InlineData(true, new string[0], null)]
    public void ChooseInput_FollowsAudioActivity(bool preferredHasSound, string[] available, string? expected) =>
        Assert.Equal(expected, AudioDevices.ChooseInput("quest", "desk", available, preferredHasSound));

    [Fact]
    public void ChooseInput_WithoutPreferred_UsesPrimary() =>
        Assert.Equal("desk", AudioDevices.ChooseInput(null, "desk", new[] { "desk", "quest" }));

    private static readonly AudioDeviceInfo[] Speakers =
        { new("spk", "Speakers (Realtek)"), new("cable", "CABLE Input (VB-Audio Virtual Cable)") };

    [Fact]
    public void WithRemembered_KeepsSwitchedOffDevice()
    {
        var list = AudioDevices.WithRemembered(Speakers, "fiio", "FiiO (2- FiiO BTR17)");
        Assert.Equal(3, list.Count);
        Assert.Equal(new AudioDeviceInfo("fiio", "FiiO (2- FiiO BTR17) (not connected)"), list[2]);
    }

    [Fact]
    public void WithRemembered_LeavesConnectedDeviceAlone() =>
        Assert.Equal(Speakers, AudioDevices.WithRemembered(Speakers, "spk", "Speakers (Realtek)"));

    [Fact]
    public void WithRemembered_NothingRemembered() =>
        Assert.Equal(Speakers, AudioDevices.WithRemembered(Speakers, null, null));

    [Fact]
    public void WithRemembered_UnknownName() =>
        Assert.Equal("remembered device (not connected)",
            AudioDevices.WithRemembered(Speakers, "gone", null)[^1].Name);

    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", "CABLE Input (VB-Audio Virtual Cable)", true)]
    [InlineData("CABLE-A Output (VB-Audio Cable A)", "CABLE-A Input (VB-Audio Cable A)", true)]
    [InlineData("Microphone (FIFINE K658 Microphone)", "CABLE Input (VB-Audio Virtual Cable)", false)]
    [InlineData("Microphone (Virtual Desktop Audio)", "CABLE Input (VB-Audio Virtual Cable)", false)]
    [InlineData("Microphone (Logi Webcam C920e)", "Speakers (Logi Webcam C920e)", false)]
    public void VirtualCableLoop_IsDetected(string input, string output, bool loop) =>
        Assert.Equal(loop, AudioDevices.IsVirtualCableLoop(input, output));

    [Fact]
    public void DigitalSilence_DistinguishesIdleFromNoiseFloor()
    {
        var silent = new byte[960];
        var floor = new byte[960];
        floor[517] = 1;
        Assert.True(MicActivityProbe.IsDigitalSilence(silent, silent.Length));
        Assert.False(MicActivityProbe.IsDigitalSilence(floor, floor.Length));
    }
}
