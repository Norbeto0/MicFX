namespace MicFX.Audio;

/// <summary>
/// Silences the signal between phrases using a speech probability per 10 ms
/// frame (RNNoise's voice-activity output), rather than signal level. A level
/// gate opens for anything loud — desk knocks, keyboard hits — while this one
/// opens only for speech.
///
/// Opens quickly when the probability crosses <see cref="Threshold"/>, stays
/// open for a hold period after the last speech frame so pauses between words
/// don't chop, then fades out. Gain changes are linear ramps per sample, so
/// there are no clicks.
///
/// Used with RNNoise the gate gets lookahead for free: the probability for a
/// frame describes the newest input, while the audio it is applied to is that
/// library's output, which lags the input by two frames. The gate is therefore
/// already opening when a word onset arrives.
/// </summary>
public sealed class VoiceGate
{
    private readonly int channels;
    private readonly int holdFrames;
    private readonly float attackStep;
    private readonly float releaseStep;
    private int holdLeft;
    private float gain;

    public float Threshold { get; set; }

    /// <summary>Current gain, 0 (closed) to 1 (open).</summary>
    public float Gain => gain;

    public VoiceGate(int sampleRate, int channels, float threshold = 0.5f,
        int holdMs = 300, float attackMs = 5f, float releaseMs = 80f, int frameMs = 10)
    {
        this.channels = channels;
        Threshold = threshold;
        holdFrames = Math.Max(0, holdMs / frameMs);
        attackStep = 1f / Math.Max(1f, attackMs * sampleRate / 1000f);
        releaseStep = 1f / Math.Max(1f, releaseMs * sampleRate / 1000f);
    }

    /// <summary>Closes the gate and forgets the hold, e.g. when it is switched on.</summary>
    public void Reset()
    {
        holdLeft = 0;
        gain = 0f;
    }

    /// <summary>Applies the gate to one interleaved frame given that frame's speech probability.</summary>
    public void ProcessFrame(float[] buffer, int offset, int frames, float speechProbability)
    {
        if (speechProbability >= Threshold) holdLeft = holdFrames + 1;
        else if (holdLeft > 0) holdLeft--;
        float target = holdLeft > 0 ? 1f : 0f;

        for (int f = 0; f < frames; f++)
        {
            if (gain < target) gain = MathF.Min(target, gain + attackStep);
            else if (gain > target) gain = MathF.Max(target, gain - releaseStep);

            int i = offset + f * channels;
            for (int ch = 0; ch < channels; ch++)
                buffer[i + ch] *= gain;
        }
    }
}
