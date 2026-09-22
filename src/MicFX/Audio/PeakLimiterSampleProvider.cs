using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Brick-wall peak limiter that keeps the output under a ceiling, so voice and
/// a loud soundboard clip playing together cannot clip into Discord or blast
/// the headphone monitor.
///
/// It looks ahead 1 ms: audio is delayed by that much while the gain is steered
/// by the loudest sample still to come, so reduction ramps in before a peak
/// arrives instead of clipping it. Gain is linked across channels to keep the
/// stereo image. Below the ceiling the gain stays at exactly 1, so ordinary
/// audio passes through bit-exact, only delayed. A final clamp at the ceiling
/// is a safety net for anything the ramp could not fully catch.
/// </summary>
public class PeakLimiterSampleProvider : ISampleProvider
{
    public const float CeilingDb = -1f;

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly int lookahead;      // in frames
    private readonly float ceiling;
    private readonly float attackCoef;
    private readonly float releaseCoef;
    private readonly float[] delay;      // interleaved, lookahead frames
    private readonly float[] peaks;      // linked peak per frame, lookahead + 1 window
    private int delayPos;
    private int peakPos;
    private float gain = 1f;
    private float minGain = 1f;

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>Added latency, in samples per channel.</summary>
    public int LatencySamples => lookahead;

    public PeakLimiterSampleProvider(ISampleProvider source, float lookaheadMs = 1f,
        float attackMs = 0.2f, float releaseMs = 80f)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        lookahead = Math.Max(1, (int)(lookaheadMs * rate / 1000f));
        ceiling = MathF.Pow(10f, CeilingDb / 20f);
        attackCoef = MathF.Exp(-1f / (attackMs * rate / 1000f));
        releaseCoef = MathF.Exp(-1f / (releaseMs * rate / 1000f));
        delay = new float[lookahead * channels];
        peaks = new float[lookahead + 1];
    }

    /// <summary>
    /// Largest gain reduction applied since the previous call, in dB (0 when
    /// the limiter was idle). Call it from the audio thread, e.g. a meter
    /// callback in the same chain.
    /// </summary>
    public float ConsumeMaxGainReductionDb()
    {
        float g = minGain;
        minGain = 1f;
        return g >= 1f ? 0f : -20f * MathF.Log10(g);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        for (int f = offset; f + channels <= offset + read; f += channels)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
                peak = MathF.Max(peak, MathF.Abs(buffer[f + ch]));

            peaks[peakPos] = peak;
            peakPos = (peakPos + 1) % peaks.Length;
            float windowMax = 0f;
            foreach (var p in peaks) windowMax = MathF.Max(windowMax, p);

            float target = windowMax > ceiling ? ceiling / windowMax : 1f;
            float coef = target < gain ? attackCoef : releaseCoef;
            gain = target + (gain - target) * coef;
            if (gain < minGain) minGain = gain;

            int d = delayPos * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                float delayed = delay[d + ch];
                delay[d + ch] = buffer[f + ch];
                buffer[f + ch] = Math.Clamp(delayed * gain, -ceiling, ceiling);
            }
            delayPos = (delayPos + 1) % lookahead;
        }
        return read;
    }
}
