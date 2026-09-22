using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// ML noise suppression using RNNoise v0.2 (xiph.org). Works on fixed
/// 480-sample (10 ms) frames at 48 kHz with one native state per channel.
/// The native library is built from the pinned official release in CI and
/// embedded; when it cannot be loaded, IsAvailable is false and the engine
/// falls back to the spectral suppressor.
///
/// Two refinements on top of the raw model, both benchmarked on real speech:
/// <list type="bullet">
/// <item><see cref="MaxReductionDb"/> blends the untreated signal back in, so
/// noise is reduced by at most that much. The dry path is delayed to match the
/// model's output exactly — without that the blend comb-filters the voice.</item>
/// <item><see cref="VoiceGateEnabled"/> silences the output between phrases
/// using the model's own speech probability.</item>
/// </list>
/// </summary>
public class RnNoiseSampleProvider : ISampleProvider, IDisposable
{
    private const int FrameSize = 480; // fixed by RNNoise: 10 ms at 48 kHz

    /// <summary>
    /// How far RNNoise v0.2's output lags its input, in samples (two frames),
    /// measured by cross-correlating output with input on real speech. The dry
    /// path must be delayed by exactly this for the strength blend to be clean.
    /// </summary>
    public const int AlgorithmicDelay = 960;

    /// <summary>At or above this, <see cref="MaxReductionDb"/> means "no limit".</summary>
    public const float MaxReductionUnlimited = 60f;

    private static class Native
    {
        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr rnnoise_create(IntPtr model);

        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        public static extern void rnnoise_destroy(IntPtr state);

        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        public static extern float rnnoise_process_frame(IntPtr state, float[] output, float[] input);
    }

    private static bool? available;

    /// <summary>Why the library could not be used, when IsAvailable is false.</summary>
    public static string? UnavailableReason { get; private set; }

    /// <summary>True when the native rnnoise library loads and works.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (available == null)
            {
                NativeLibraryLoader.EnsureRegistered();
                try
                {
                    var probe = Native.rnnoise_create(IntPtr.Zero);
                    if (probe != IntPtr.Zero) Native.rnnoise_destroy(probe);
                    available = probe != IntPtr.Zero;
                    if (probe == IntPtr.Zero) UnavailableReason = "rnnoise_create returned null";
                }
                catch (Exception ex)
                {
                    available = false;
                    UnavailableReason = NativeLibraryLoader.LoadError ?? ex.Message;
                }
            }
            return available.Value;
        }
    }

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly IntPtr[] states;
    private readonly object stateLock = new();
    private readonly float[] hopIn;
    private readonly float[] frameIn = new float[FrameSize];
    private readonly float[] frameOut = new float[FrameSize];
    private readonly float[] frameMix;
    private readonly float[][] dryLine;
    private readonly float[] outQueue;
    private readonly VoiceGate gate;
    private int dryPos;
    private int queueCount;
    private int queueRead;
    private bool enabled;
    private bool disposed;
    private bool gateEnabled;
    private volatile bool resetPending;
    private volatile bool gateResetPending;

    public bool Enabled
    {
        get => enabled;
        set
        {
            bool next = value && IsAvailable && !disposed;
            // Start clean: stale model state and delay lines would otherwise
            // replay a few frames of audio from before it was switched off.
            if (next && !enabled) resetPending = true;
            enabled = next;
        }
    }

    /// <summary>
    /// Upper bound on noise reduction in dB (6–60). Below
    /// <see cref="MaxReductionUnlimited"/> the delay-matched dry signal is mixed
    /// in at that level; lower values sound more natural but leave more noise.
    /// </summary>
    public float MaxReductionDb { get; set; } = MaxReductionUnlimited;

    /// <summary>Silence the output between phrases using the model's speech probability.</summary>
    public bool VoiceGateEnabled
    {
        get => gateEnabled;
        set
        {
            if (value && !gateEnabled) gateResetPending = true;
            gateEnabled = value;
        }
    }

    /// <summary>Speech probability of the most recent frame (max over channels).</summary>
    public float LastVoiceProbability { get; private set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>
    /// Voice gate settings, calibrated on real speech (CMU ARCTIC) mixed with
    /// fan, hiss, hum and keyboard noise at 0–20 dB SNR. A 0.6 threshold never
    /// opened on noise alone (0.5 did, up to 4% of the time) while keeping
    /// 99.0–99.6% of speech frames at 10–20 dB SNR; hold time did not change
    /// speech retention, and 200 ms returns to silence faster than 300 ms.
    /// </summary>
    public RnNoiseSampleProvider(ISampleProvider source, float gateThreshold = 0.6f, int gateHoldMs = 200)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        states = new IntPtr[channels];
        hopIn = new float[FrameSize * channels];
        frameMix = new float[FrameSize * channels];
        outQueue = new float[FrameSize * channels * 4];
        dryLine = new float[channels][];
        for (int ch = 0; ch < channels; ch++)
            dryLine[ch] = new float[AlgorithmicDelay];
        gate = new VoiceGate(source.WaveFormat.SampleRate, channels, gateThreshold, gateHoldMs);
        gate.Reset();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!enabled || disposed)
        {
            queueCount = 0;
            queueRead = 0;
            return source.Read(buffer, offset, count);
        }

        int written = 0;
        while (written < count)
        {
            if (queueCount > 0)
            {
                int n = Math.Min(queueCount, count - written);
                for (int i = 0; i < n; i++)
                    buffer[offset + written + i] = outQueue[(queueRead + i) % outQueue.Length];
                queueRead = (queueRead + n) % outQueue.Length;
                queueCount -= n;
                written += n;
                continue;
            }

            int need = FrameSize * channels;
            int got = ReadFull(hopIn, need);
            if (got == 0) break;
            for (int i = got; i < need; i++) hopIn[i] = 0f;
            ProcessFrame();
        }
        return written;
    }

    private int ReadFull(float[] dest, int need)
    {
        int total = 0;
        while (total < need)
        {
            int n = source.Read(dest, total, need - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private void ProcessFrame()
    {
        lock (stateLock)
        {
            if (disposed) return;

            if (resetPending)
            {
                resetPending = false;
                DestroyStates();
                foreach (var line in dryLine) Array.Clear(line);
                dryPos = 0;
            }
            if (gateResetPending)
            {
                gateResetPending = false;
                gate.Reset();
            }

            float maxReduction = MaxReductionDb;
            float dryMix = maxReduction >= MaxReductionUnlimited ? 0f : MathF.Pow(10f, -maxReduction / 20f);
            float wetMix = 1f - dryMix;
            float vad = 0f;

            for (int ch = 0; ch < channels; ch++)
            {
                if (states[ch] == IntPtr.Zero)
                    states[ch] = Native.rnnoise_create(IntPtr.Zero);

                // RNNoise expects 16-bit-range float samples
                for (int i = 0; i < FrameSize; i++)
                    frameIn[i] = hopIn[i * channels + ch] * 32768f;

                vad = MathF.Max(vad, Native.rnnoise_process_frame(states[ch], frameOut, frameIn));

                var line = dryLine[ch];
                for (int i = 0; i < FrameSize; i++)
                {
                    // Read the sample written AlgorithmicDelay samples ago, then
                    // replace it: the dry path lines up exactly with the model output.
                    int idx = (dryPos + i) % AlgorithmicDelay;
                    float dry = line[idx];
                    line[idx] = hopIn[i * channels + ch];
                    frameMix[i * channels + ch] = wetMix * (frameOut[i] / 32768f) + dryMix * dry;
                }
            }
            dryPos = (dryPos + FrameSize) % AlgorithmicDelay;
            LastVoiceProbability = vad;

            if (gateEnabled)
                gate.ProcessFrame(frameMix, 0, FrameSize, vad);

            int total = FrameSize * channels;
            for (int i = 0; i < total; i++)
                outQueue[(queueRead + queueCount + i) % outQueue.Length] = frameMix[i];
            queueCount += total;
        }
    }

    private void DestroyStates()
    {
        for (int ch = 0; ch < channels; ch++)
        {
            if (states[ch] != IntPtr.Zero)
            {
                Native.rnnoise_destroy(states[ch]);
                states[ch] = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            disposed = true;
            enabled = false;
            DestroyStates();
        }
    }
}
