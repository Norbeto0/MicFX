using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Broadcast-style voice leveler: downward compressor with auto make-up gain,
/// driven by a single 0–100 "amount" control (higher = tighter, louder).
/// </summary>
public class CompressorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly float attackCoef;
    private readonly float releaseCoef;

    private float thresholdDb = -21f;
    private float ratio = 4f;
    private float makeupDb = 7.9f;
    private float envelopeDb = -100f;

    public bool Enabled { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public CompressorSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        attackCoef = MathF.Exp(-1f / (0.005f * rate));  // 5 ms
        releaseCoef = MathF.Exp(-1f / (0.090f * rate)); // 90 ms
    }

    /// <summary>0–100: scales threshold, ratio and make-up together.</summary>
    public void SetAmount(float amount)
    {
        float t = Math.Clamp(amount, 0f, 100f) / 100f;
        thresholdDb = -12f - 18f * t;
        ratio = 2f + 4f * t;
        makeupDb = -thresholdDb * (1f - 1f / ratio) * 0.55f;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!Enabled)
        {
            envelopeDb = -100f;
            return read;
        }

        for (int i = 0; i < read; i += channels)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels && i + ch < read; ch++)
                peak = MathF.Max(peak, MathF.Abs(buffer[offset + i + ch]));

            float peakDb = 20f * MathF.Log10(MathF.Max(peak, 1e-6f));
            float coef = peakDb > envelopeDb ? attackCoef : releaseCoef;
            envelopeDb = coef * envelopeDb + (1f - coef) * peakDb;

            float over = envelopeDb - thresholdDb;
            float reductionDb = over > 0f ? over * (1f - 1f / ratio) : 0f;
            float gain = MathF.Pow(10f, (makeupDb - reductionDb) / 20f);

            for (int ch = 0; ch < channels && i + ch < read; ch++)
            {
                float y = buffer[offset + i + ch] * gain;
                // soft safety limiter so make-up gain can't clip hard
                buffer[offset + i + ch] = y > 0.95f || y < -0.95f ? MathF.Tanh(y) : y;
            }
        }
        return read;
    }
}
