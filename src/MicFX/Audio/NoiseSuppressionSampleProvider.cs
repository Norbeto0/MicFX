using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Spectral noise suppression: a streaming STFT (512-point, 50% overlap,
/// sqrt-Hann analysis/synthesis) with a per-bin noise power estimate learned
/// in pauses and Wiener gains driven by a decision-directed a-priori SNR
/// (Ephraim-Malah): the speech estimate is smoothed over frames, so noise
/// bins that momentarily poke above the estimate no longer open briefly and
/// ring as "musical noise". Frames judged to be noise only are held at the
/// floor gain, so pauses carry the same noise, just quieter by ReductionDb.
/// Removes steady background noise (fans, hum, AC, hiss). Adds ~5 ms
/// latency. Pure managed code.
///
/// Measured on real speech with fan, hiss and hum noise at 10-20 dB SNR, against
/// the instantaneous-SNR gains used up to 1.10.0: pauses are cut by exactly
/// ReductionDb (was ~8 dB at any setting) and the residual's spectral kurtosis
/// matches the input noise (log kurtosis ratio 0.0, was 1.4).
/// </summary>
public class NoiseSuppressionSampleProvider : ISampleProvider
{
    private const int FftSize = 512;
    private const int FftM = 9;          // 2^9 = 512
    private const int Hop = FftSize / 2;
    private const int Bins = FftSize / 2 + 1;

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly float[] window = new float[FftSize];
    private readonly float fftScale;     // measured once — makes us independent of the library's scaling convention

    // per-channel state
    // Weight of the previous frame's speech estimate in the a-priori SNR.
    // 0.98 is the classic value; lower values let musical noise back in.
    private const float DecisionDirected = 0.98f;

    // Noise learning: the first frames are averaged plainly; after that one
    // frame may raise a bin's estimate by at most 18% (it contributes at most
    // 4x the current estimate). A voice that dips below the frame detector's
    // threshold (a held note, a soft syllable) would otherwise be learned as
    // noise in a single frame and suppressed from then on, while a real rise
    // in noise can still be followed at 10 dB per ~14 frames (75 ms).
    private const int NoiseWarmupFrames = 16;
    private const float NoiseUpdateCap = 4f;

    private readonly float[][] history;  // last FftSize input samples
    private readonly float[][] ola;      // overlap-add accumulator
    private readonly float[][] noisePower;   // per-bin noise power estimate
    private readonly float[][] prevSpeech;   // previous frame's speech amplitude estimate
    private readonly float[] noiseEnergy;
    private readonly int[] speechHang;   // frames to wait after speech before learning noise again
    private readonly int[] noiseFrames;  // noise frames learned so far (for the warm-up average)
    private bool wasEnabled;

    private readonly Complex[] fft = new Complex[FftSize];
    private readonly float[] mag = new float[Bins];
    private readonly float[] power = new float[Bins];
    private readonly float[] hopIn;      // interleaved staging buffer
    private readonly float[] outQueue;   // interleaved processed samples ready to hand out
    private int queueCount;
    private int queueRead;

    public bool Enabled { get; set; }

    /// <summary>How hard residual noise is pushed down (gain floor), 6–30 dB.</summary>
    public float ReductionDb { get; set; } = 18f;

    public WaveFormat WaveFormat => source.WaveFormat;

    public NoiseSuppressionSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;

        for (int i = 0; i < FftSize; i++)
            window[i] = MathF.Sqrt(0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / FftSize));

        history = NewPerChannel(FftSize);
        ola = NewPerChannel(FftSize);
        noisePower = NewPerChannel(Bins);
        prevSpeech = NewPerChannel(Bins);
        noiseEnergy = new float[channels];
        speechHang = new int[channels];
        noiseFrames = new int[channels];
        ResetState();

        hopIn = new float[Hop * channels];
        outQueue = new float[Hop * channels * 4];

        fftScale = MeasureRoundTripScale();
    }

    /// <summary>Forgets everything learned, so a re-enable starts clean instead of replaying stale audio.</summary>
    private void ResetState()
    {
        for (int ch = 0; ch < channels; ch++)
        {
            Array.Clear(history[ch]);
            Array.Clear(ola[ch]);
            Array.Clear(noisePower[ch]); // zero = not learned yet: passes audio until it is
            Array.Clear(prevSpeech[ch]);
        }
        Array.Fill(noiseEnergy, 1f); // starts high, snaps down to the real floor
        Array.Clear(speechHang);
        Array.Clear(noiseFrames);
        queueCount = 0;
        queueRead = 0;
    }

    private float[][] NewPerChannel(int size)
    {
        var arr = new float[channels][];
        for (int ch = 0; ch < channels; ch++) arr[ch] = new float[size];
        return arr;
    }

    /// <summary>
    /// Runs an impulse through FFT+IFFT to find the library's round-trip scale
    /// factor, so reconstruction is exact whatever convention NAudio uses.
    /// </summary>
    private static float MeasureRoundTripScale()
    {
        var buf = new Complex[FftSize];
        buf[0].X = 1f;
        FastFourierTransform.FFT(true, FftM, buf);
        FastFourierTransform.FFT(false, FftM, buf);
        float roundTrip = buf[0].X;
        return roundTrip > 1e-12f ? 1f / roundTrip : 1f;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!Enabled)
        {
            wasEnabled = false;
            return source.Read(buffer, offset, count);
        }
        if (!wasEnabled)
        {
            ResetState();
            wasEnabled = true;
        }

        int written = 0;
        while (written < count)
        {
            if (queueCount > 0)
            {
                int n = Math.Min(queueCount, count - written);
                for (int i = 0; i < n; i++)
                    buffer[offset + written + i] = outQueue[(queueRead + i) % outQueue.Length];
                queueRead = (queueRead + n) % outQueue.Length;
                queueCount -= n;
                written += n;
                continue;
            }

            int need = Hop * channels;
            int got = ReadFull(hopIn, need);
            if (got == 0) break;
            for (int i = got; i < need; i++) hopIn[i] = 0f;
            ProcessHop();
        }
        return written;
    }

    private int ReadFull(float[] dest, int need)
    {
        int total = 0;
        while (total < need)
        {
            int n = source.Read(dest, total, need - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private void ProcessHop()
    {
        float floorGain = MathF.Pow(10f, -ReductionDb / 20f);

        for (int ch = 0; ch < channels; ch++)
        {
            var hist = history[ch];

            // slide the analysis window forward by one hop
            Array.Copy(hist, Hop, hist, 0, FftSize - Hop);
            for (int i = 0; i < Hop; i++)
                hist[FftSize - Hop + i] = hopIn[i * channels + ch];

            for (int i = 0; i < FftSize; i++)
            {
                fft[i].X = hist[i] * window[i];
                fft[i].Y = 0f;
            }
            FastFourierTransform.FFT(true, FftM, fft);

            float energy = 0f;
            for (int b = 0; b < Bins; b++)
            {
                power[b] = fft[b].X * fft[b].X + fft[b].Y * fft[b].Y;
                mag[b] = MathF.Sqrt(power[b]);
                if (b > 0) energy += mag[b];
            }
            energy /= Bins - 1;

            // adaptive noise floor: snaps down instantly, creeps up slowly
            // (creep is slow on purpose so sustained speech isn't learned as noise)
            if (energy < noiseEnergy[ch]) noiseEnergy[ch] = energy;
            else noiseEnergy[ch] = MathF.Min(noiseEnergy[ch] * 1.0015f + 1e-9f, 1f);

            // hangover: after any speech-loud frame, wait ~40 ms before trusting
            // quiet frames as noise, so syllable dips don't poison the estimate
            if (energy >= 2.5f * noiseEnergy[ch]) speechHang[ch] = 8;
            else if (speechHang[ch] > 0) speechHang[ch]--;
            bool learnNoise = speechHang[ch] == 0 && energy < 2.5f * noiseEnergy[ch];

            var lambda = noisePower[ch];
            var prev = prevSpeech[ch];
            bool warmup = false;
            if (learnNoise)
            {
                noiseFrames[ch] = Math.Min(noiseFrames[ch] + 1, NoiseWarmupFrames + 1);
                warmup = noiseFrames[ch] <= NoiseWarmupFrames;
            }
            for (int b = 0; b < Bins; b++)
            {
                if (learnNoise)
                {
                    lambda[b] = warmup
                        ? lambda[b] + (power[b] - lambda[b]) / noiseFrames[ch]
                        : 0.94f * lambda[b] + 0.06f * MathF.Min(power[b], NoiseUpdateCap * lambda[b]);
                }

                // a-posteriori SNR of this frame, and the a-priori SNR blended
                // with the previous frame's speech estimate (decision-directed)
                float noiseP = MathF.Max(lambda[b], 1e-20f);
                float post = MathF.Min(power[b] / noiseP, 1e4f);
                float prior = DecisionDirected * prev[b] * prev[b] / noiseP
                            + (1f - DecisionDirected) * MathF.Max(post - 1f, 0f);
                prior = MathF.Min(prior, 1e8f); // before the noise is learned; keeps the ratio finite
                float g = MathF.Max(prior / (1f + prior), floorGain);
                prev[b] = g * mag[b];

                // Pauses: the floor for every bin, so nothing flickers in them.
                if (learnNoise) g = floorGain;

                fft[b].X *= g;
                fft[b].Y *= g;
                if (b > 0 && b < FftSize - b)
                {
                    fft[FftSize - b].X *= g;
                    fft[FftSize - b].Y *= g;
                }
            }

            FastFourierTransform.FFT(false, FftM, fft);

            var acc = ola[ch];
            for (int i = 0; i < FftSize; i++)
                acc[i] += fft[i].X * fftScale * window[i];
        }

        // the first Hop samples of each accumulator are now complete — emit
        // them interleaved, then slide the accumulators forward
        for (int i = 0; i < Hop; i++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                int w = (queueRead + queueCount) % outQueue.Length;
                outQueue[w] = ola[ch][i];
                queueCount++;
            }
        }
        for (int ch = 0; ch < channels; ch++)
        {
            var acc = ola[ch];
            Array.Copy(acc, Hop, acc, 0, FftSize - Hop);
            Array.Clear(acc, FftSize - Hop, Hop);
        }
    }
}
