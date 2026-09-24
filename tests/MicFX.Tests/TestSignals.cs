using NAudio.Wave;

namespace MicFX.Tests;

/// <summary>Deterministic test sources and small measurement helpers.</summary>
internal static class TestSignals
{
    public const int Rate = 48000;

    public static ISampleProvider Mono(Func<long, float> f) => new FuncSource(1, n => (f(n), 0f));

    public static ISampleProvider Stereo(Func<long, (float L, float R)> f) => new FuncSource(2, f);

    public static ISampleProvider FromArray(float[] samples, int channels = 1) => new ArraySource(samples, channels);

    /// <summary>Reads everything a provider yields, up to maxSamples.</summary>
    public static float[] ReadAll(ISampleProvider p, int maxSamples)
    {
        var result = new float[maxSamples];
        int total = 0;
        while (total < maxSamples)
        {
            int n = p.Read(result, total, Math.Min(4800, maxSamples - total));
            if (n == 0) break;
            total += n;
        }
        Array.Resize(ref result, total);
        return result;
    }

    /// <summary>Raw little-endian float32 samples, as written by numpy's tofile.</summary>
    public static float[] ReadF32(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    public static double RmsDb(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (var v in x) sum += v * v;
        return 10 * Math.Log10(sum / Math.Max(1, x.Length) + 1e-20);
    }

    private sealed class FuncSource : ISampleProvider
    {
        private readonly int channels;
        private readonly Func<long, (float L, float R)> f;
        private long n;
        public WaveFormat WaveFormat { get; }

        public FuncSource(int channels, Func<long, (float, float)> f)
        {
            this.channels = channels;
            this.f = f;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i + channels <= count; i += channels)
            {
                var (l, r) = f(n++);
                buffer[offset + i] = l;
                if (channels == 2) buffer[offset + i + 1] = r;
            }
            return count - count % channels;
        }
    }

    private sealed class ArraySource : ISampleProvider
    {
        private readonly float[] data;
        private int pos;
        public WaveFormat WaveFormat { get; }

        public ArraySource(float[] data, int channels)
        {
            this.data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - pos);
            Array.Copy(data, pos, buffer, offset, n);
            pos += n;
            return n;
        }
    }
}
