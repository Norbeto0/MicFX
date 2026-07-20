using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Graphic equalizer with a configurable band count (10–20), log-spaced from
/// 31 Hz to 16 kHz, built from per-channel biquad peaking filters whose Q
/// tracks the band spacing. All band data lives in one immutable set swapped
/// atomically, so the audio thread never sees a half-rebuilt EQ.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    public const int MinBands = 10;
    public const int MaxBands = 20;

    private class BandSet
    {
        public float[] Frequencies = Array.Empty<float>();
        public float[] Gains = Array.Empty<float>();
        public BiQuadFilter[][] Filters = Array.Empty<BiQuadFilter[]>(); // [band][channel]
        public float Q;
    }

    private readonly ISampleProvider source;
    private readonly int channels;
    private volatile BandSet bands;

    public WaveFormat WaveFormat => source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source, int bandCount, float[]? gains = null)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        bands = Build(bandCount, gains);
    }

    /// <summary>Log-spaced centres, 31.25 Hz … 16 kHz (the classic octave bands when count = 10).</summary>
    public static float[] BuildFrequencies(int count)
    {
        count = Math.Clamp(count, MinBands, MaxBands);
        var freqs = new float[count];
        for (int i = 0; i < count; i++)
            freqs[i] = 31.25f * MathF.Pow(512f, i / (float)(count - 1));
        return freqs;
    }

    private BandSet Build(int count, float[]? gains)
    {
        var freqs = BuildFrequencies(count);
        count = freqs.Length;
        float bandOctaves = 9f / (count - 1);
        float ratio = MathF.Pow(2f, bandOctaves);
        float q = MathF.Sqrt(ratio) / (ratio - 1f);

        var set = new BandSet
        {
            Frequencies = freqs,
            Gains = new float[count],
            Filters = new BiQuadFilter[count][],
            Q = q
        };
        for (int b = 0; b < count; b++)
        {
            set.Gains[b] = gains != null && b < gains.Length ? Math.Clamp(gains[b], -15f, 15f) : 0f;
            set.Filters[b] = new BiQuadFilter[channels];
            for (int ch = 0; ch < channels; ch++)
                set.Filters[b][ch] = BiQuadFilter.PeakingEQ(
                    source.WaveFormat.SampleRate, freqs[b], q, set.Gains[b]);
        }
        return set;
    }

    /// <summary>Change the number of bands (and optionally seed gains) while running.</summary>
    public void SetBands(int count, float[]? gains) => bands = Build(count, gains);

    public void SetGain(int band, float db)
    {
        var set = bands;
        if (band < 0 || band >= set.Gains.Length) return;
        set.Gains[band] = Math.Clamp(db, -15f, 15f);
        var filters = new BiQuadFilter[channels];
        for (int ch = 0; ch < channels; ch++)
            filters[ch] = BiQuadFilter.PeakingEQ(
                source.WaveFormat.SampleRate, set.Frequencies[band], set.Q, set.Gains[band]);
        set.Filters[band] = filters;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        var set = bands;
        for (int i = 0; i < read; i++)
        {
            int ch = i % channels;
            float sample = buffer[offset + i];
            for (int b = 0; b < set.Gains.Length; b++)
            {
                if (set.Gains[b] == 0f) continue;
                sample = set.Filters[b][ch].Transform(sample);
            }
            buffer[offset + i] = sample;
        }
        return read;
    }
}
