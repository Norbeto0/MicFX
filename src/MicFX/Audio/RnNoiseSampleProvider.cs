using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// ML noise suppression using RNNoise (xiph.org) — the same model family
/// Discord's standard noise suppression is built on. Works on fixed
/// 480-sample (10 ms) frames at 48 kHz with one native state per channel.
/// The native library is built from the pinned official source in CI; when
/// it can't be loaded, IsAvailable is false and the engine falls back to
/// the spectral suppressor.
/// </summary>
public class RnNoiseSampleProvider : ISampleProvider, IDisposable
{
    private const int FrameSize = 480; // fixed by RNNoise: 10 ms at 48 kHz

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
    private readonly float[] outQueue;
    private int queueCount;
    private int queueRead;
    private bool enabled;
    private bool disposed;

    // RNNoise suppresses best around a normal speech level (~-14 dBFS peak,
    // measured); quiet mics get much weaker suppression. Track the input
    // envelope and boost into that operating point, undoing the boost on the
    // way out so the stream level is untouched.
    private const float TargetPeak = 6500f / 32768f;
    private float envelope;
    private float normGain = 1f;

    public bool Enabled
    {
        get => enabled;
        set => enabled = value && IsAvailable && !disposed;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public RnNoiseSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        states = new IntPtr[channels];
        hopIn = new float[FrameSize * channels];
        outQueue = new float[FrameSize * channels * 4];
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
        float framePeak = 0f;
        for (int i = 0; i < hopIn.Length; i++)
            framePeak = MathF.Max(framePeak, MathF.Abs(hopIn[i]));
        envelope = MathF.Max(framePeak, envelope * 0.995f);
        float desired = envelope > 1e-4f ? Math.Clamp(TargetPeak / envelope, 1f, 32f) : 1f;
        normGain = 0.9f * normGain + 0.1f * desired;
        float scaleIn = 32768f * normGain;

        lock (stateLock)
        {
            if (disposed) return;
            for (int ch = 0; ch < channels; ch++)
            {
                if (states[ch] == IntPtr.Zero)
                    states[ch] = Native.rnnoise_create(IntPtr.Zero);

                // RNNoise expects 16-bit-range float samples
                for (int i = 0; i < FrameSize; i++)
                    frameIn[i] = hopIn[i * channels + ch] * scaleIn;

                Native.rnnoise_process_frame(states[ch], frameOut, frameIn);

                for (int i = 0; i < FrameSize; i++)
                {
                    int w = (queueRead + queueCount + i * channels + ch) % outQueue.Length;
                    outQueue[w] = frameOut[i] / scaleIn;
                }
            }
        }
        queueCount += FrameSize * channels;
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            disposed = true;
            enabled = false;
            for (int ch = 0; ch < channels; ch++)
            {
                if (states[ch] != IntPtr.Zero)
                {
                    Native.rnnoise_destroy(states[ch]);
                    states[ch] = IntPtr.Zero;
                }
            }
        }
    }
}
