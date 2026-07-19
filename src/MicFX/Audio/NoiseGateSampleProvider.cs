using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Downward noise gate with a fast-attack envelope follower and smooth
/// open/close ramps so word onsets are kept and the tail doesn't click.
/// </summary>
public class NoiseGateSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly float envAttack;
    private readonly float envRelease;
    private readonly float openStep;
    private readonly float closeStep;

    private float envelope;
    private float gain = 1f;

    public bool Enabled { get; set; }
    public float ThresholdDb { get; set; } = -45f;
    public WaveFormat WaveFormat => source.WaveFormat;

    public NoiseGateSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        envAttack = MathF.Exp(-1f / (0.001f * rate));  // 1 ms
        envRelease = MathF.Exp(-1f / (0.050f * rate)); // 50 ms
        openStep = 1f / (0.003f * rate);               // opens in ~3 ms
        closeStep = 1f / (0.120f * rate);              // closes in ~120 ms
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled)
        {
            gain = 1f;
            return read;
        }

        float threshold = MathF.Pow(10f, ThresholdDb / 20f);
        for (int i = 0; i < read; i += channels)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels && i + ch < read; ch++)
                peak = MathF.Max(peak, MathF.Abs(buffer[offset + i + ch]));

            float coef = peak > envelope ? envAttack : envRelease;
            envelope = coef * envelope + (1f - coef) * peak;

            float target = envelope >= threshold ? 1f : 0f;
            if (target > gain) gain = MathF.Min(target, gain + openStep);
            else if (target < gain) gain = MathF.Max(target, gain - closeStep);

            for (int ch = 0; ch < channels && i + ch < read; ch++)
                buffer[offset + i + ch] *= gain;
        }
        return read;
    }
}
