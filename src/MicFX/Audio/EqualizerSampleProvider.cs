using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// 10-band graphic equalizer built from per-channel biquad peaking filters.
/// Gains can be changed live from the UI thread; filter references are swapped
/// atomically so the audio thread never sees a half-built filter.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    public static readonly float[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private const float Q = 1.4f; // ~one octave per band

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly BiQuadFilter[][] filters; // [band][channel]
    private readonly float[] gains = new float[Frequencies.Length];

    public WaveFormat WaveFormat => source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        filters = new BiQuadFilter[Frequencies.Length][];
        for (int band = 0; band < Frequencies.Length; band++)
        {
            filters[band] = new BiQuadFilter[channels];
            RebuildBand(band);
        }
    }

    public void SetGain(int band, float db)
    {
        gains[band] = Math.Clamp(db, -15f, 15f);
        RebuildBand(band);
    }

    private void RebuildBand(int band)
    {
        for (int ch = 0; ch < channels; ch++)
            filters[band][ch] = BiQuadFilter.PeakingEQ(source.WaveFormat.SampleRate, Frequencies[band], Q, gains[band]);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        for (int i = 0; i < read; i++)
        {
            int ch = i % channels;
            float sample = buffer[offset + i];
            for (int band = 0; band < Frequencies.Length; band++)
            {
                if (gains[band] == 0f) continue;
                sample = filters[band][ch].Transform(sample);
            }
            buffer[offset + i] = sample;
        }
        return read;
    }
}
