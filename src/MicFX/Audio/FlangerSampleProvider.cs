using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Flanger ("alien" voice): a short delay line whose delay time is swept by
/// an LFO, mixed back with feedback. Classic metallic swoosh.
/// </summary>
public class FlangerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly int bufferLen;
    private readonly float[][] delay;
    private int writePos;
    private double lfoPhase;

    public bool Enabled { get; set; }
    public float Rate { get; set; } = 0.8f;      // LFO Hz
    public float Feedback { get; set; } = 0.45f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public FlangerSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        bufferLen = source.WaveFormat.SampleRate / 40; // 25 ms
        delay = new float[channels][];
        for (int ch = 0; ch < channels; ch++)
            delay[ch] = new float[bufferLen];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled) return read;

        int rate = source.WaveFormat.SampleRate;
        double lfoStep = 2 * Math.PI * Rate / rate;

        for (int i = 0; i < read; i += channels)
        {
            float delaySamples =
                (0.002f + 0.004f * (0.5f + 0.5f * (float)Math.Sin(lfoPhase))) * rate;
            lfoPhase += lfoStep;
            if (lfoPhase > 2 * Math.PI) lfoPhase -= 2 * Math.PI;

            int whole = (int)delaySamples;
            float frac = delaySamples - whole;

            for (int ch = 0; ch < channels && i + ch < read; ch++)
            {
                var line = delay[ch];
                int r0 = (writePos - whole + bufferLen) % bufferLen;
                int r1 = (r0 - 1 + bufferLen) % bufferLen;
                float delayed = line[r0] * (1f - frac) + line[r1] * frac;

                float dry = buffer[offset + i + ch];
                line[writePos] = dry + delayed * Feedback;
                buffer[offset + i + ch] = 0.6f * dry + 0.6f * delayed;
            }
            writePos = (writePos + 1) % bufferLen;
        }
        return read;
    }
}
