using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Whisper voice: strips most of the voiced (pitched) signal and replaces it
/// with envelope-modulated filtered noise, so speech keeps its rhythm and
/// sibilance but loses its pitch.
/// </summary>
public class WhisperSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly BiQuadFilter[] highPass;
    private readonly Random rng = new();
    private readonly float envAttack;
    private readonly float envRelease;
    private float envelope;
    private float noiseState;

    public bool Enabled { get; set; }
    public float NoiseLevel { get; set; } = 1f; // 0.4..1.2
    public WaveFormat WaveFormat => source.WaveFormat;

    public WhisperSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        highPass = new BiQuadFilter[channels];
        for (int ch = 0; ch < channels; ch++)
            highPass[ch] = BiQuadFilter.HighPassFilter(rate, 1400f, 0.7f);
        envAttack = MathF.Exp(-1f / (0.001f * rate));
        envRelease = MathF.Exp(-1f / (0.060f * rate));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled) return read;

        for (int i = 0; i < read; i += channels)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels && i + ch < read; ch++)
                peak = MathF.Max(peak, MathF.Abs(buffer[offset + i + ch]));
            float coef = peak > envelope ? envAttack : envRelease;
            envelope = coef * envelope + (1f - coef) * peak;

            // one-pole lowpass keeps the noise breathy instead of harsh
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            noiseState = 0.8f * noiseState + 0.2f * noise;

            for (int ch = 0; ch < channels && i + ch < read; ch++)
            {
                float breath = noiseState * envelope * NoiseLevel * 3f;
                float sibilance = highPass[ch].Transform(buffer[offset + i + ch]) * 0.3f;
                buffer[offset + i + ch] = breath + sibilance;
            }
        }
        return read;
    }
}
