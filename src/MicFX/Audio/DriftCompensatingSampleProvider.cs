using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Compensates for clock drift between the capture device and the output
/// device. The microphone and the output (a virtual cable) run on independent
/// clocks; when the mic produces samples slightly faster than the output
/// consumes them, the capture buffer fills up and end-to-end latency grows
/// without bound — up to a full second or more before the buffer cap is hit —
/// until the app is restarted.
///
/// This wrapper sits directly on the capture buffer (same sample rate and
/// channel count, so the accounting is exact) and, on the audio thread, checks
/// the buffer's fill level on every Read. When the backlog exceeds a ceiling it
/// discards the oldest queued audio so latency snaps back to a small target.
/// For real hardware drift these corrections are small and happen at most every
/// few minutes.
/// </summary>
public class DriftCompensatingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly BufferedWaveProvider buffer;
    private readonly int channels;
    private readonly int bytesPerSample;
    private readonly int targetBytes;
    private readonly int ceilingBytes;
    private float[] scratch = Array.Empty<float>();

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>Number of drift corrections applied so far (for diagnostics/tests).</summary>
    public int Corrections { get; private set; }

    public DriftCompensatingSampleProvider(BufferedWaveProvider buffer, int targetMs, int ceilingMs)
    {
        this.buffer = buffer;
        source = buffer.ToSampleProvider();
        channels = buffer.WaveFormat.Channels;
        bytesPerSample = buffer.WaveFormat.BitsPerSample / 8; // one channel sample
        int bytesPerSecond = buffer.WaveFormat.AverageBytesPerSecond;
        targetBytes = bytesPerSecond * targetMs / 1000;
        ceilingBytes = bytesPerSecond * ceilingMs / 1000;
    }

    public int Read(float[] output, int offset, int count)
    {
        if (buffer.BufferedBytes > ceilingBytes)
            DiscardExcess();
        return source.Read(output, offset, count);
    }

    private void DiscardExcess()
    {
        // Drop everything above the target so latency returns to the target,
        // not just below the ceiling — otherwise slow drift would trigger a
        // correction on nearly every Read once the ceiling is reached.
        int excessBytes = buffer.BufferedBytes - targetBytes;
        int floats = excessBytes / bytesPerSample;
        floats -= floats % channels; // keep whole frames so L/R stay aligned
        if (floats <= 0) return;

        if (scratch.Length < floats)
            scratch = new float[floats];

        int discarded = 0;
        while (discarded < floats)
        {
            int n = source.Read(scratch, 0, floats - discarded);
            if (n == 0) break;
            discarded += n;
        }
        Corrections++;
    }
}
