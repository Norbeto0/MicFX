using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Ghost voice: a reverse echo. Audio is recorded in chunks and the previous
/// chunk is mixed back in *reversed* and darkened, producing the eerie
/// "pre-echo" swell heard in horror soundtracks.
/// </summary>
public class GhostSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly int chunkLen;
    private float[][] current;
    private float[][] previous;
    private readonly float[] lpState;
    private int pos;
    private bool cleared = true;

    public bool Enabled { get; set; }
    public float Mix { get; set; } = 0.5f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public GhostSampleProvider(ISampleProvider source, float chunkMs = 600f)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        chunkLen = Math.Max(1, (int)(source.WaveFormat.SampleRate * chunkMs / 1000f));
        current = NewChunk();
        previous = NewChunk();
        lpState = new float[channels];
    }

    private float[][] NewChunk()
    {
        var c = new float[channels][];
        for (int ch = 0; ch < channels; ch++) c[ch] = new float[chunkLen];
        return c;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled)
        {
            if (!cleared)
            {
                foreach (var c in current) Array.Clear(c);
                foreach (var c in previous) Array.Clear(c);
                Array.Clear(lpState);
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
                float rev = previous[ch][chunkLen - 1 - pos];
                lpState[ch] = 0.7f * lpState[ch] + 0.3f * rev; // darken the echo
                buffer[offset + i + ch] = dry + lpState[ch] * Mix;
                current[ch][pos] = dry;
            }
            pos++;
            if (pos >= chunkLen)
            {
                (previous, current) = (current, previous);
                pos = 0;
            }
        }
        return read;
    }
}
