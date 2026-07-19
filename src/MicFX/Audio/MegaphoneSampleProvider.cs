using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Megaphone / radio voice: telephone-band filtering (350 Hz – 3 kHz)
/// plus tanh drive distortion.
/// </summary>
public class MegaphoneSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly BiQuadFilter[] highPass;
    private readonly BiQuadFilter[] lowPass;

    public bool Enabled { get; set; }
    public float Drive { get; set; } = 5f; // 2..8
    public WaveFormat WaveFormat => source.WaveFormat;

    public MegaphoneSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        highPass = new BiQuadFilter[channels];
        lowPass = new BiQuadFilter[channels];
        for (int ch = 0; ch < channels; ch++)
        {
            highPass[ch] = BiQuadFilter.HighPassFilter(rate, 350f, 0.7f);
            lowPass[ch] = BiQuadFilter.LowPassFilter(rate, 3000f, 0.7f);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled) return read;

        float drive = MathF.Max(Drive, 1f);
        float norm = 1f / MathF.Tanh(drive);
        for (int i = 0; i < read; i++)
        {
            int ch = i % channels;
            float s = buffer[offset + i];
            s = lowPass[ch].Transform(highPass[ch].Transform(s));
            buffer[offset + i] = MathF.Tanh(s * drive) * norm * 0.85f;
        }
        return read;
    }
}
