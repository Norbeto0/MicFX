using NAudio.Dsp;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Spectral noise suppression: a streaming STFT (512-point, 50% overlap,
/// sqrt-Hann analysis/synthesis) with an adaptive per-bin noise-floor estimate
/// and Wiener-style gains. Removes steady background noise (fans, hum, AC)
/// while speech is passing. Adds ~5 ms latency. Pure managed code.
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
    private readonly float[][] history;  // last FftSize input samples
    private readonly float[][] ola;      // overlap-add accumulator
    private readonly float[][] noise;    // per-bin noise magnitude estimate
    private readonly float[][] gainSmooth;
    private readonly float[] noiseEnergy;
    private readonly int[] speechHang;   // frames to wait after speech before learning noise again

    private readonly Complex[] fft = new Complex[FftSize];
    private readonly float[] mag = new float[Bins];
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
        noise = NewPerChannel(Bins);
        gainSmooth = NewPerChannel(Bins);
        foreach (var g in gainSmooth) Array.Fill(g, 1f);
        noiseEnergy = new float[channels];
        Array.Fill(noiseEnergy, 1f); // starts high, snaps down to the real floor
        speechHang = new int[channels];

        hopIn = new float[Hop * channels];
        outQueue = new float[Hop * channels * 4];

        fftScale = MeasureRoundTripScale();
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
            queueCount = 0;
            queueRead = 0;
            return source.Read(buffer, offset, count);
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
                mag[b] = MathF.Sqrt(fft[b].X * fft[b].X + fft[b].Y * fft[b].Y);
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

            var nz = noise[ch];
            var gs = gainSmooth[ch];
            for (int b = 0; b < Bins; b++)
            {
                if (learnNoise)
                    nz[b] = 0.94f * nz[b] + 0.06f * mag[b];

                // Wiener-style gain from per-bin SNR — gentler on speech than
                // plain spectral subtraction
                float ratio = mag[b] / MathF.Max(1.2f * nz[b], 1e-9f);
                float snr = MathF.Max(ratio * ratio - 1f, 0f);
                float g = Math.Clamp(snr / (snr + 1f), floorGain, 1f);

                // fast attack (speech onsets survive), slower release
                gs[b] = g > gs[b] ? 0.3f * gs[b] + 0.7f * g : 0.7f * gs[b] + 0.3f * g;

                fft[b].X *= gs[b];
                fft[b].Y *= gs[b];
                if (b > 0 && b < FftSize - b)
                {
                    fft[FftSize - b].X *= gs[b];
                    fft[FftSize - b].Y *= gs[b];
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
