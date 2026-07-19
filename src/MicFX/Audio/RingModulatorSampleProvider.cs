using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Classic sine ring modulator — the "robot"/Dalek voice. The oscillator
/// advances once per frame so all channels stay phase-coherent.
/// </summary>
public class RingModulatorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private double phase;

    public bool Enabled { get; set; }
    public float Frequency { get; set; } = 50f;
    public float Mix { get; set; } = 0.9f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public RingModulatorSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled) return read;

        double step = 2 * Math.PI * Frequency / source.WaveFormat.SampleRate;
        for (int i = 0; i < read; i += channels)
        {
            float mod = (float)Math.Sin(phase);
            phase += step;
            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;

            for (int ch = 0; ch < channels && i + ch < read; ch++)
            {
                float x = buffer[offset + i + ch];
                buffer[offset + i + ch] = x * (1f - Mix) + x * mod * Mix;
            }
        }
        return read;
    }
}
