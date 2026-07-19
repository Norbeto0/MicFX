using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Feedback delay line ("cave" echo). Per-channel circular buffers; the delay
/// lines are cleared whenever the effect is disabled so stale audio doesn't
/// play back when it is re-enabled later.
/// </summary>
public class EchoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly float[][] delayLines;
    private readonly int delaySamples;
    private int pos;
    private bool cleared = true;

    public bool Enabled { get; set; }
    public float Feedback { get; set; } = 0.35f;
    public float Mix { get; set; } = 0.4f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public EchoSampleProvider(ISampleProvider source, float delayMs = 280f)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        delaySamples = Math.Max(1, (int)(source.WaveFormat.SampleRate * delayMs / 1000f));
        delayLines = new float[channels][];
        for (int ch = 0; ch < channels; ch++)
            delayLines[ch] = new float[delaySamples];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled)
        {
            if (!cleared)
            {
                foreach (var line in delayLines) Array.Clear(line);
                pos = 0;
                cleared = true;
            }
            return read;
        }

        cleared = false;
        for (int i = 0; i < read; i += channels)
        {
            for (int ch = 0; ch < channels && i + ch < read; ch++)
            {
                float dry = buffer[offset + i + ch];
                float delayed = delayLines[ch][pos];
                buffer[offset + i + ch] = dry + delayed * Mix;
                delayLines[ch][pos] = dry + delayed * Feedback;
            }
            pos++;
            if (pos >= delaySamples) pos = 0;
        }
        return read;
    }
}
