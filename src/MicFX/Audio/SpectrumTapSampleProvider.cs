using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Pass-through that keeps a rolling mono window of the most recent samples
/// so the UI can render a spectrum. The UI polls with CopyLatest.
/// </summary>
public class SpectrumTapSampleProvider : ISampleProvider
{
    public const int WindowSize = 2048;

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly float[] ring = new float[WindowSize];
    private int pos;

    public WaveFormat WaveFormat => source.WaveFormat;

    public SpectrumTapSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        lock (ring)
        {
            for (int i = 0; i + channels <= read; i += channels)
            {
                float mono = 0f;
                for (int ch = 0; ch < channels; ch++)
                    mono += buffer[offset + i + ch];
                ring[pos] = mono / channels;
                pos = (pos + 1) % WindowSize;
            }
        }
        return read;
    }

    /// <summary>Copies the newest WindowSize samples, oldest first.</summary>
    public void CopyLatest(float[] dest)
    {
        lock (ring)
        {
            int start = pos;
            for (int i = 0; i < WindowSize; i++)
                dest[i] = ring[(start + i) % WindowSize];
        }
    }
}
